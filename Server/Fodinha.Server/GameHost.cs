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

        private readonly ServerCore core;
        private readonly ILogger<GameHost> logger;
        private readonly Channel<Action> inbox = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });

        public GameHost(ServerCore core, ILogger<GameHost> logger)
        {
            this.core = core;
            this.logger = logger;
        }

        public ServerCore Core => core;

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
        public async Task HandleAsync(WebSocket socket, CancellationToken aborted)
        {
            var link = new SocketLink(socket);
            var sender = link.RunSenderAsync(aborted);
            Post(() => core.OnConnected(link));

            var buffer = new byte[4096];
            var message = new StringBuilder();
            int messageBytes = 0;
            try
            {
                while (socket.State == WebSocketState.Open && !aborted.IsCancellationRequested)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), aborted);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    if (result.MessageType != WebSocketMessageType.Text) continue;

                    messageBytes += result.Count;
                    if (messageBytes > Protocol.MaxClientMessageBytes)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Mensagem grande demais", CancellationToken.None);
                        break;
                    }
                    message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (!result.EndOfMessage) continue;

                    string text = message.ToString();
                    message.Clear();
                    messageBytes = 0;
                    Post(() => core.OnMessage(link, text));
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
            private readonly Channel<string> outbox = Channel.CreateBounded<string>(new BoundedChannelOptions(512)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.DropOldest
            });

            public SocketLink(WebSocket socket) => this.socket = socket;

            public void Send(string json) => outbox.Writer.TryWrite(json);

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
