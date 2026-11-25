using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fodinha.Hosting;
using Fodinha.Net;
using Microsoft.AspNetCore.Builder;
using NUnit.Framework;

namespace Fodinha.Server.Tests
{
    /// <summary>Ataques contra o servidor de verdade (HTTP + WebSocket), como um invasor faria pela internet.</summary>
    public class SecurityIntegrationTests
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

        private Uri Ws => new Uri("ws://127.0.0.1:" + port + Protocol.Path);

        private static async Task<bool> TryConnect(ClientWebSocket ws, Uri uri)
        {
            try
            {
                using var cts = new CancellationTokenSource(5000);
                await ws.ConnectAsync(uri, cts.Token);
                return ws.State == WebSocketState.Open;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Ataque: abrir centenas de conexões do mesmo IP para esgotar o servidor.</summary>
        [Test]
        public async Task MuitasConexoesDoMesmoIp_SaoRecusadas()
        {
            var sockets = new List<ClientWebSocket>();
            int opened = 0;
            try
            {
                for (int i = 0; i < 60; i++)
                {
                    var ws = new ClientWebSocket();
                    sockets.Add(ws);
                    if (await TryConnect(ws, Ws)) opened++;
                }
                Assert.That(opened, Is.LessThanOrEqualTo(30), "Cada IP tem um limite de conexões ao mesmo tempo.");
            }
            finally
            {
                foreach (var s in sockets) s.Abort();
            }
            await Task.Delay(300);
            using var later = new ClientWebSocket();
            Assert.That(await TryConnect(later, Ws), Is.True, "Fechando as conexões, libera.");
            later.Abort();
        }

        /// <summary>Ataque: um site falso usando o nosso servidor (o navegador da vítima manda Origin do site falso).</summary>
        [Test]
        public async Task ConexaoVindaDeOutroSite_EhRecusada()
        {
            using var evil = new ClientWebSocket();
            evil.Options.SetRequestHeader("Origin", "https://site-falso.example");
            Assert.That(await TryConnect(evil, Ws), Is.False);

            using var same = new ClientWebSocket();
            same.Options.SetRequestHeader("Origin", "http://127.0.0.1:" + port);
            Assert.That(await TryConnect(same, Ws), Is.True, "O próprio site do jogo pode.");
            same.Abort();

            using var app = new ClientWebSocket(); // app de celular não manda Origin
            Assert.That(await TryConnect(app, Ws), Is.True);
            app.Abort();
        }

        /// <summary>Ataque: enxurrada pela conexão de verdade.</summary>
        [Test]
        public async Task EnxurradaPelaConexao_DerrubaOAtacante()
        {
            using var ws = new ClientWebSocket();
            Assert.That(await TryConnect(ws, Ws), Is.True);
            var hello = Encoding.UTF8.GetBytes(Protocol.Encode(Protocol.ToServer.Hello, new HelloMsg { name = "Flood", version = Protocol.Version }));
            await ws.SendAsync(new ArraySegment<byte>(hello), WebSocketMessageType.Text, true, CancellationToken.None);
            var ping = Encoding.UTF8.GetBytes(Protocol.Encode(Protocol.ToServer.GetProfile));
            try
            {
                for (int i = 0; i < 5000 && ws.State == WebSocketState.Open; i++)
                    await ws.SendAsync(new ArraySegment<byte>(ping), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception) { /* servidor derrubou: esperado */ }

            // Lê até o SERVIDOR fechar (o nosso tempo esgotado não conta como fechamento).
            var buffer = new byte[64 * 1024];
            using var cts = new CancellationTokenSource(5000);
            bool closedByServer = false;
            try
            {
                while (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseSent)
                {
                    var r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                    if (r.MessageType == WebSocketMessageType.Close) { closedByServer = true; break; }
                }
                if (ws.State == WebSocketState.Closed || ws.State == WebSocketState.CloseReceived) closedByServer = true;
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { closedByServer = true; }
            Assert.That(closedByServer, Is.True, "Quem inunda o servidor é desconectado.");
        }

        /// <summary>Site: cabeçalhos contra clickjacking (site falso por cima do jogo) e afins.</summary>
        [Test]
        public async Task Site_TemCabecalhosDeSeguranca()
        {
            using var http = new HttpClient();
            var r = await http.GetAsync("http://127.0.0.1:" + port + "/info");
            Assert.That(r.Headers.TryGetValues("X-Content-Type-Options", out var nosniff) && nosniff.Contains("nosniff"), Is.True);
            Assert.That(r.Headers.TryGetValues("X-Frame-Options", out var frame) && frame.Contains("SAMEORIGIN"), Is.True);
            Assert.That(r.Headers.TryGetValues("Content-Security-Policy", out var csp) && csp.First().Contains("frame-ancestors 'self'"), Is.True);
            Assert.That(r.Headers.Contains("Referrer-Policy"), Is.True);
            Assert.That(r.Headers.Contains("Server"), Is.False, "Não anuncia qual servidor web é.");
        }

        /// <summary>/health não pode revelar nada sensível.</summary>
        [Test]
        public async Task Health_NaoVazaSegredos()
        {
            using var http = new HttpClient();
            string body = await http.GetStringAsync("http://127.0.0.1:" + port + "/health");
            Assert.That(body.ToLowerInvariant(), Does.Not.Contain("password").And.Not.Contain("token").And.Not.Contain("vip"));
        }
    }
}
