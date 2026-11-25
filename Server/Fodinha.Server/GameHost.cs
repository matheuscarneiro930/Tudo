using System;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Fodinha.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fodinha.Server
{
    /// <summary>
    /// Liga os WebSockets ao <see cref="ServerCore"/>. Tudo que mexe no estado do jogo passa por uma fila única
    /// processada por um só laço (que também avança o relógio), então o núcleo nunca é acessado em paralelo.
    /// </summary>
    public sealed class GameHost : BackgroundService
    {
        private const int TickMilliseconds = 50;

        /// <summary>Mais que isso por segundo numa conexão é ataque: derruba na hora (antes de encher a fila).</summary>
        private const int HardMessagesPerSecond = 60;

        private readonly ServerCore core;
        private readonly ServerOptions options;
        private readonly ILogger<GameHost> logger;
        private readonly Channel<Action> inbox = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> perAddress =
            new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
        private int totalConnections;

        public GameHost(ServerCore core, ServerOptions options, ILogger<GameHost> logger)
        {
            this.core = core;
            this.options = options ?? new ServerOptions();
            this.logger = logger;
        }

        public ServerCore Core => core;

        public int OpenConnections => Volatile.Read(ref totalConnections);

        /// <summary>Reserva uma vaga de conexão para o IP. Falso se o IP (ou o servidor) já está no limite.</summary>
        public bool TryEnter(string address)
        {
            if (Interlocked.Increment(ref totalConnections) > options.MaxConnections)
            {
                Interlocked.Decrement(ref totalConnections);
                return false;
            }
            int count = perAddress.AddOrUpdate(address, 1, (_, c) => c + 1);
            if (count > options.MaxConnectionsPerAddress)
            {
                Exit(address);
                return false;
            }
            return true;
        }

        public void Exit(string address)
        {
            Interlocked.Decrement(ref totalConnections);
            if (perAddress.AddOrUpdate(address, 0, (_, c) => Math.Max(0, c - 1)) == 0)
                perAddress.TryRemove(new System.Collections.Generic.KeyValuePair<string, int>(address, 0));
        }

        /// <summary>Agenda uma ação no laço do jogo.</summary>
        public void Post(Action action) => inbox.Writer.TryWrite(action);

        /// <summary>Executa algo no laço do jogo e devolve o resultado (usado em /health e testes).</summary>
        public Task<T> Query<T>(Func<ServerCore, T> query)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(() =>
            {
                try { tcs.SetResult(query(core)); }
                catch (Exception e) { tcs.SetException(e); }
            });
            return tcs.Task;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var clock = Stopwatch.StartNew();
            double last = 0;
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(TickMilliseconds));
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    while (inbox.Reader.TryRead(out var action)) Run(action);

                    double t = clock.Elapsed.TotalSeconds;
                    float dt = (float)Math.Min(1.0, t - last);
                    last = t;
                    Run(() => core.Tick(dt));

                    await timer.WaitForNextTickAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) { }
        }

        private void Run(Action action)
        {
            try { action(); }
            catch (Exception e) { logger.LogError(e, "Erro no laço do jogo"); }
        }

        /// <summary>Atende uma conexão WebSocket até ela fechar.</summary>
        public async Task HandleAsync(WebSocket socket, CancellationToken aborted, string address = "desconhecido")
        {
            var link = new SocketLink(socket, address);
            var sender = link.RunSenderAsync(aborted);
            Post(() => core.OnConnected(link));

            var buffer = new byte[4096];
            // Junta os BYTES da mensagem e só depois converte (um "é" pode vir partido entre dois pedaços).
            var message = new System.IO.MemoryStream();
            var window = Stopwatch.StartNew();
            int inWindow = 0;
            try
            {
                while (socket.State == WebSocketState.Open && !aborted.IsCancellationRequested)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), aborted);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        // O jogo só usa texto: binário é lixo (ou ataque).
                        await socket.CloseAsync(WebSocketCloseStatus.InvalidMessageType, "Só texto", CancellationToken.None);
                        break;
                    }

                    if (message.Length + result.Count > Protocol.MaxClientMessageBytes)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Mensagem grande demais", CancellationToken.None);
                        break;
                    }
                    message.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage) continue;

                    // Enxurrada grosseira: derruba antes de encher a fila do jogo.
                    if (window.ElapsedMilliseconds >= 1000) { window.Restart(); inWindow = 0; }
                    if (++inWindow > HardMessagesPerSecond)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Mensagens demais", CancellationToken.None);
                        break;
                    }

                    string text;
                    try { text = new UTF8Encoding(false, true).GetString(message.GetBuffer(), 0, (int)message.Length); }
                    catch (DecoderFallbackException) { text = null; }
                    message.SetLength(0);
                    if (text != null) Post(() => core.OnMessage(link, text));
                }
            }
            catch (WebSocketException) { }
            catch (OperationCanceledException) { }
            finally
            {
                Post(() => core.OnDisconnected(link));
                link.Complete();
                try { await sender; } catch { /* conexão já caiu */ }
                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                {
                    try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); }
                    catch { /* ignorado */ }
                }
            }
        }

        /// <summary>Conexão real: as mensagens vão para uma fila e uma tarefa as envia em ordem.</summary>
        private sealed class SocketLink : IClientLink
        {
            private readonly WebSocket socket;
            // Fila de saída curta: quem não lê o que o servidor manda (de propósito, para encher a memória)
            // é desconectado quando a fila enche. O app reconecta e recebe o estado completo de novo.
            private readonly Channel<string> outbox = Channel.CreateBounded<string>(new BoundedChannelOptions(128)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait
            });

            public SocketLink(WebSocket socket, string address)
            {
                this.socket = socket;
                Address = address;
            }

            public string Address { get; }

            public void Send(string json)
            {
                if (!outbox.Writer.TryWrite(json)) Close();
            }

            public void Close()
            {
                outbox.Writer.TryComplete();
                try { socket.Abort(); } catch { /* ignorado */ }
            }

            public void Complete() => outbox.Writer.TryComplete();

            public async Task RunSenderAsync(CancellationToken aborted)
            {
                await foreach (var json in outbox.Reader.ReadAllAsync(aborted))
                {
                    if (socket.State != WebSocketState.Open) break;
                    var bytes = Encoding.UTF8.GetBytes(json);
                    await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, aborted);
                }
            }
        }
    }
}
