using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fodinha.Core;
using Fodinha.Hosting;
using Fodinha.Net;
using Microsoft.AspNetCore.Builder;
using NUnit.Framework;

namespace Fodinha.Server.Tests
{
    /// <summary>Servidor de verdade (Kestrel + WebSocket) com clientes de verdade jogando uma partida inteira.</summary>
    public class WebSocketIntegrationTests
    {
        private WebApplication app;
        private int port;

        [OneTimeSetUp]
        public async Task StartServer()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();

            app = Program.BuildApp(Array.Empty<string>(), new ServerOptions { Pacing = PacingSettings.Instant }, "http://127.0.0.1:" + port);
            await app.StartAsync();
        }

        [OneTimeTearDown]
        public async Task StopServer()
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }

        private sealed class WsClient : IAsyncDisposable
        {
            private readonly ClientWebSocket ws = new ClientWebSocket();
            private readonly CancellationTokenSource cts = new CancellationTokenSource();
            private readonly object gate = new object();
            private Task receiver;
            public WelcomeMsg Welcome;
            public RoomStateDto Room;
            public MatchUpdateDto Match;
            public ErrorMsg Error;
            public int Messages;

            public async Task ConnectAsync(int port, string name)
            {
                await ws.ConnectAsync(new Uri("ws://127.0.0.1:" + port + Protocol.Path), cts.Token);
                receiver = Task.Run(ReceiveLoop);
                await SendAsync(Protocol.ToServer.Hello, new HelloMsg { name = name, version = Protocol.Version });
            }

            public Task SendAsync(string type, object payload = null)
            {
                var bytes = Encoding.UTF8.GetBytes(Protocol.Encode(type, payload));
                return ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
            }

            private async Task ReceiveLoop()
            {
                var buffer = new byte[64 * 1024];
                var sb = new StringBuilder();
                try
                {
                    while (ws.State == WebSocketState.Open)
                    {
                        var r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                        if (r.MessageType == WebSocketMessageType.Close) break;
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
                        if (!r.EndOfMessage) continue;
                        Protocol.TryDecode(sb.ToString(), out var env);
                        sb.Clear();
                        lock (gate)
                        {
                            Messages++;
                            switch (env.t)
                            {
                                case Protocol.ToClient.Welcome: Welcome = Protocol.Payload<WelcomeMsg>(env); break;
                                case Protocol.ToClient.RoomState: Room = Protocol.Payload<RoomStateDto>(env); break;
                                case Protocol.ToClient.Match: Match = Protocol.Payload<MatchUpdateDto>(env); break;
                                case Protocol.ToClient.Error: Error = Protocol.Payload<ErrorMsg>(env); break;
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (WebSocketException) { }
            }

            public T Read<T>(Func<WsClient, T> f)
            {
                lock (gate) return f(this);
            }

            public async ValueTask DisposeAsync()
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
                cts.Cancel();
                if (receiver != null) await receiver;
                ws.Dispose();
            }
        }

        private static async Task WaitFor(Func<bool> condition, double seconds = 10)
        {
            var sw = Stopwatch.StartNew();
            while (!condition())
            {
                if (sw.Elapsed.TotalSeconds > seconds) Assert.Fail("Tempo esgotado esperando condição.");
                await Task.Delay(10);
            }
        }

        [Test]
        public async Task Health_Responde()
        {
            using var http = new HttpClient();
            var text = await http.GetStringAsync("http://127.0.0.1:" + port + "/health");
            Assert.That(text, Does.Contain("\"ok\":true"));
        }

        [Test]
        public async Task TresJogadores_MaisBots_PartidaInteiraPelaRede()
        {
            await using var ana = new WsClient();
            await using var bia = new WsClient();
            await using var caio = new WsClient();
            await ana.ConnectAsync(port, "Ana");
            await bia.ConnectAsync(port, "Bia");
            await caio.ConnectAsync(port, "Caio");
            await WaitFor(() => ana.Read(c => c.Welcome) != null && bia.Read(c => c.Welcome) != null && caio.Read(c => c.Welcome) != null);

            await ana.SendAsync(Protocol.ToServer.CreateRoom, new CreateRoomMsg { options = new RoomOptions { MaxPlayers = 5, IsPublic = false } });
            await WaitFor(() => ana.Read(c => c.Room) != null);
            string code = ana.Read(c => c.Room.code);

            await bia.SendAsync(Protocol.ToServer.JoinRoom, new JoinRoomMsg { code = code });
            await caio.SendAsync(Protocol.ToServer.JoinRoom, new JoinRoomMsg { code = code });
            await ana.SendAsync(Protocol.ToServer.AddBot);
            await ana.SendAsync(Protocol.ToServer.AddBot);
            await WaitFor(() => ana.Read(c => c.Room.members.Count) == 5);

            await ana.SendAsync(Protocol.ToServer.StartMatch);
            var all = new[] { ana, bia, caio };
            await WaitFor(() => all.All(c => c.Read(x => x.Match) != null));

            var sw = Stopwatch.StartNew();
            var lastActed = new Dictionary<WsClient, string>();
            while (true)
            {
                Assert.That(sw.Elapsed.TotalSeconds, Is.LessThan(90), "A partida demorou demais.");
                bool over = all.All(c => c.Read(x => x.Match.view.phase) == (int)MatchPhase.MatchOver);
                if (over) break;

                foreach (var c in all)
                {
                    var v = c.Read(x => x.Match.view);
                    if (v.current != v.viewer) continue;
                    // Evita repetir o mesmo pedido antes de chegar a atualização do servidor.
                    string key = v.round + "/" + v.trick + "/" + v.phase + "/" + v.hand.Count;
                    if (lastActed.TryGetValue(c, out var k) && k == key) continue;
                    lastActed[c] = key;
                    if (v.phase == (int)MatchPhase.Betting && v.allowed.Count > 0)
                        await c.SendAsync(Protocol.ToServer.Bet, new BetMsg { value = v.allowed[0] });
                    else if (v.phase == (int)MatchPhase.Playing && v.hand.Count > 0)
                        await c.SendAsync(Protocol.ToServer.Play, new PlayMsg { card = v.hand[0] });
                }
                await Task.Delay(5);
            }

            int winner = ana.Read(c => c.Match.view.winner);
            Assert.That(winner, Is.InRange(0, 4));
            Assert.That(bia.Read(c => c.Match.view.winner), Is.EqualTo(winner));
            await WaitFor(() => ana.Read(c => c.Room.inMatch) == false);
        }
    }
}
