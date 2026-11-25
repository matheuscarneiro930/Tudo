using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Fodinha.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Fodinha.Server.Tests
{
    /// <summary>Painel do dono, visitas ao site e cópias de segurança no servidor de verdade (HTTP).</summary>
    public class DashboardIntegrationTests
    {
        private const string Key = "senha-do-painel-de-teste";
        private const string Browser = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36";
        private WebApplication app;
        private string baseUrl;
        private HttpClient http;

        internal static (WebApplication app, string url) Start(string adminKey)
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            string url = "http://127.0.0.1:" + port;
            var app = Program.BuildApp(Array.Empty<string>(), new ServerOptions { Pacing = PacingSettings.Instant, AdminKey = adminKey }, url,
                                       new MemoryAccountStore(), null, new MemoryStatsStore(), new MemoryBackupStore());
            app.StartAsync().GetAwaiter().GetResult();
            return (app, url);
        }

        [OneTimeSetUp]
        public void StartServer()
        {
            (app, baseUrl) = Start(Key);
            http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        }

        [OneTimeTearDown]
        public async Task StopServer()
        {
            http.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }

        private HttpRequestMessage Req(HttpMethod method, string path, string password = Key, string ua = Browser)
        {
            var r = new HttpRequestMessage(method, baseUrl + path);
            if (password != null)
                r.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("dono:" + password)));
            if (ua != null) r.Headers.TryAddWithoutValidation("User-Agent", ua);
            return r;
        }

        private StatsBook Stats => app.Services.GetRequiredService<StatsBook>();
        private GameHost Host => app.Services.GetRequiredService<GameHost>();

        [Test]
        public async Task SemSenha_PedeSenha()
        {
            var res = await http.SendAsync(Req(HttpMethod.Get, Dashboard.Path, password: null));
            Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(res.Headers.WwwAuthenticate.ToString(), Does.Contain("Basic"));
        }

        [Test]
        public async Task SenhaErrada_Recusa()
        {
            var res = await http.SendAsync(Req(HttpMethod.Get, Dashboard.Path, password: "errada"));
            Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task SenhaCerta_MostraOPainel_SemCacheNemBusca()
        {
            var res = await http.SendAsync(Req(HttpMethod.Get, Dashboard.Path));
            Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            string html = await res.Content.ReadAsStringAsync();
            Assert.That(html, Does.Contain("Painel do Fodinha"));
            Assert.That(html, Does.Contain("Últimos 30 dias"));
            Assert.That(html, Does.Contain("Cópias de segurança"));
            Assert.That(res.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(res.Headers.GetValues("X-Robots-Tag").First(), Does.Contain("noindex"));
        }

        [Test]
        public async Task Dados_EmJson()
        {
            var res = await http.SendAsync(Req(HttpMethod.Get, Dashboard.Path + "/dados.json"));
            Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await res.Content.ReadAsStringAsync(), Does.Contain("\"dias\""));
            var anon = await http.SendAsync(Req(HttpMethod.Get, Dashboard.Path + "/dados.json", password: null));
            Assert.That(anon.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task Visitas_ContaPessoasDiferentes_IgnoraRobos_EConvite()
        {
            long visits = Stats.Recent(1)[0][Metric.Visits];
            long visitors = Stats.Recent(1)[0][Metric.Visitors];
            long invite = Stats.Recent(1)[0][Metric.InviteVisits];

            await http.SendAsync(Req(HttpMethod.Get, "/", password: null));
            await http.SendAsync(Req(HttpMethod.Get, "/index.html", password: null));
            await http.SendAsync(Req(HttpMethod.Get, "/?origem=convite", password: null));
            await http.SendAsync(Req(HttpMethod.Get, "/", password: null, ua: "WhatsApp/2.23.20.0 A"));
            await http.SendAsync(Req(HttpMethod.Get, "/", password: null, ua: "Mozilla/5.0 (compatible; Googlebot/2.1)"));
            await http.SendAsync(Req(HttpMethod.Get, "/health", password: null));

            var d = Stats.Recent(1)[0];
            Assert.That(d[Metric.Visits] - visits, Is.EqualTo(3), "3 aberturas da página por um navegador; robôs e /health não contam.");
            Assert.That(d[Metric.Visitors] - visitors, Is.LessThanOrEqualTo(1), "Mesma pessoa (mesmo IP) conta uma vez no dia.");
            Assert.That(d[Metric.Visitors], Is.GreaterThanOrEqualTo(1));
            Assert.That(d[Metric.InviteVisits] - invite, Is.EqualTo(1));
            Assert.That(d[Metric.Visits + "_pc"], Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public async Task Backup_BaixarAgora_TemAsContas()
        {
            await Host.Query(core => core.Accounts.Create("p-baixar", "t-baixar", "Baixar"));
            var res = await http.SendAsync(Req(HttpMethod.Get, Dashboard.Path + "/backup"));
            Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(res.Content.Headers.ContentDisposition?.FileName, Does.Contain("fodinha-contas"));
            var accounts = BackupFile.Read(await res.Content.ReadAsByteArrayAsync());
            Assert.That(accounts.Any(a => a.Id == "p-baixar"), Is.True);

            var anon = await http.SendAsync(Req(HttpMethod.Get, Dashboard.Path + "/backup", password: null));
            Assert.That(anon.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "Sem senha, nada de cópia.");
        }

        [Test]
        public async Task Backup_Diario_UmPorDia()
        {
            var backups = app.Services.GetRequiredService<BackupService>();
            await backups.EnsureTodayAsync();
            var again = await backups.EnsureTodayAsync();
            Assert.That(again, Is.Null, "Já tem a de hoje: não faz outra.");
            var list = await backups.Store.ListAsync();
            Assert.That(list.Count(b => b.Name == StatsBook.DayOf(DateTime.UtcNow).ToString("yyyy-MM-dd")), Is.EqualTo(1));
            Assert.That(backups.LastError, Is.Null);
        }

        private HttpRequestMessage RestorePost(string name, string confirm, string origin)
        {
            var r = Req(HttpMethod.Post, Dashboard.Path + "/restaurar");
            if (origin != null) r.Headers.TryAddWithoutValidation("Origin", origin);
            var form = new MultipartFormDataContent();
            form.Add(new StringContent(name), "nome");
            form.Add(new StringContent(confirm), "confirmar");
            r.Content = form;
            return r;
        }

        [Test]
        public async Task Restaurar_ProtegidoEConfirmado_VoltaAsMoedas()
        {
            var backups = app.Services.GetRequiredService<BackupService>();
            await Host.Query(core => core.Accounts.Create("p-rest", "t-rest", "Rest"));
            await backups.TakeAsync("teste-restaurar");
            await Host.Query(core => core.Accounts.AddCoins(core.Accounts.FindById("p-rest"), -400));

            // Outro site tentando mandar o formulário pelo navegador do dono: recusado.
            var cross = await http.SendAsync(RestorePost("teste-restaurar", "RESTAURAR", "https://site-malicioso.example"));
            Assert.That(cross.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            var noOrigin = await http.SendAsync(RestorePost("teste-restaurar", "RESTAURAR", null));
            Assert.That(noOrigin.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

            // Sem digitar RESTAURAR: nada muda.
            var unconfirmed = await http.SendAsync(RestorePost("teste-restaurar", "sim", baseUrl));
            Assert.That((int)unconfirmed.StatusCode, Is.EqualTo(302));
            Assert.That(await Host.Query(core => core.Accounts.FindById("p-rest").Coins), Is.EqualTo(100));

            var ok = await http.SendAsync(RestorePost("teste-restaurar", "restaurar", baseUrl));
            Assert.That((int)ok.StatusCode, Is.EqualTo(302));
            Assert.That(Uri.UnescapeDataString(ok.Headers.Location.ToString()), Does.Contain("Restaurado"));
            Assert.That(await Host.Query(core => core.Accounts.FindById("p-rest").Coins), Is.EqualTo(500));

            var list = await backups.Store.ListAsync();
            Assert.That(list.Any(b => b.Name.Contains("antes-de-restaurar")), Is.True, "Guardou como estava antes (para desfazer).");
        }

        [Test]
        public async Task Restaurar_ArquivoInvalido_NaoMexeEmNada()
        {
            await Host.Query(core => core.Accounts.Create("p-inval", "t-inval", "Inval"));
            var r = Req(HttpMethod.Post, Dashboard.Path + "/restaurar");
            r.Headers.TryAddWithoutValidation("Origin", baseUrl);
            var form = new MultipartFormDataContent();
            form.Add(new StringContent("RESTAURAR"), "confirmar");
            form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("lixo lixo lixo lixo")), "arquivo", "copia.json.gz");
            r.Content = form;
            var res = await http.SendAsync(r);
            Assert.That((int)res.StatusCode, Is.EqualTo(302));
            Assert.That(Uri.UnescapeDataString(res.Headers.Location.ToString()), Does.Contain("recusado"));
            Assert.That(await Host.Query(core => core.Accounts.FindById("p-inval").Coins), Is.EqualTo(500));
        }

        [Test]
        public void SenhaDoPainel_LeituraDoCabecalho()
        {
            Assert.That(AdminGate.PasswordFrom("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("x:abc:def"))), Is.EqualTo("abc:def"));
            Assert.That(AdminGate.PasswordFrom("Bearer abc"), Is.Null);
            Assert.That(AdminGate.PasswordFrom("Basic %%%"), Is.EqualTo(""));
            Assert.That(AdminGate.PasswordFrom(null), Is.Null);
        }
    }

    /// <summary>Painel sem senha configurada não existe; chute de senha bloqueia.</summary>
    public class DashboardLockTests
    {
        [Test]
        public async Task SemSenhaConfigurada_PainelNaoExiste()
        {
            foreach (var key in new[] { "", "curta" })
            {
                var (app, url) = DashboardIntegrationTests.Start(key);
                try
                {
                    using var http = new HttpClient();
                    var r = new HttpRequestMessage(HttpMethod.Get, url + Dashboard.Path);
                    r.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("a:" + key)));
                    Assert.That((await http.SendAsync(r)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
                }
                finally
                {
                    await app.StopAsync();
                    await app.DisposeAsync();
                }
            }
        }

        [Test]
        public async Task ChuteDeSenha_BloqueiaMesmoComASenhaCerta()
        {
            const string key = "senha-certa-bem-grande";
            var (app, url) = DashboardIntegrationTests.Start(key);
            try
            {
                using var http = new HttpClient();
                HttpStatusCode Send(string pass)
                {
                    var r = new HttpRequestMessage(HttpMethod.Get, url + Dashboard.Path);
                    r.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("a:" + pass)));
                    return http.SendAsync(r).GetAwaiter().GetResult().StatusCode;
                }
                for (int i = 0; i < AdminGate.MaxFailuresPerAddress; i++) Assert.That(Send("chute" + i), Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(Send(key), Is.EqualTo((HttpStatusCode)429), "Depois de 10 erros, nem a senha certa entra por uma hora.");
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }

        [Test]
        public void Bloqueio_ContaPorIp_ESomaGeral()
        {
            double t = 0;
            var gate = new AdminGate(() => t);
            string Auth(string p) => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("a:" + p));
            const string key = "senha-certa-bem-grande";
            for (int i = 0; i < AdminGate.MaxFailuresPerAddress; i++) gate.Check(key, Auth("x"), "1.1.1.1");
            Assert.That(gate.Check(key, Auth(key), "1.1.1.1"), Is.EqualTo(AdminGate.Result.Locked));
            Assert.That(gate.Check(key, Auth(key), "2.2.2.2"), Is.EqualTo(AdminGate.Result.Ok), "Outro IP segue entrando.");
            Assert.That(gate.Check(key, null, "3.3.3.3"), Is.EqualTo(AdminGate.Result.Missing), "Sem senha não conta como erro.");
            t += 3601;
            Assert.That(gate.Check(key, Auth(key), "1.1.1.1"), Is.EqualTo(AdminGate.Result.Ok), "Depois de uma hora, libera.");
        }
    }
}

namespace Fodinha.Server.Tests
{
    /// <summary>Gera o painel com números de exemplo num arquivo (para olhar o visual). Rodar à mão.</summary>
    public class DashboardPreview
    {
        [Test, Explicit("Só gera um arquivo de exemplo")]
        public void GerarPainelDeExemplo()
        {
            string path = System.Environment.GetEnvironmentVariable("FODINHA_PAINEL_EXEMPLO");
            if (string.IsNullOrEmpty(path)) Assert.Ignore("Defina FODINHA_PAINEL_EXEMPLO com o caminho do .html");
            var utc = DateTime.UtcNow.AddDays(-29);
            var stats = new StatsBook(() => utc);
            var rng = new Random(7);
            for (int day = 0; day < 30; day++, utc = utc.AddDays(1))
            {
                double growth = 1 + day / 6.0;
                stats.Add(Metric.Visitors, (long)(rng.Next(20, 40) * growth));
                stats.Add(Metric.InviteVisitors, (long)(rng.Next(2, 9) * growth));
                stats.Add(Metric.Players, (long)(rng.Next(15, 30) * growth));
                stats.Add(Metric.Players + "_android", (long)(rng.Next(8, 18) * growth));
                stats.Add(Metric.Players + "_site-iphone", (long)(rng.Next(3, 8) * growth));
                stats.Add(Metric.NewAccounts, (long)(rng.Next(4, 12) * growth));
                stats.Add(Metric.Matches, (long)(rng.Next(10, 25) * growth));
                stats.Add(Metric.Rewards, (long)(rng.Next(5, 20) * growth));
                stats.Add(Metric.Chats, (long)(rng.Next(30, 90) * growth));
                stats.Peak(Metric.PeakOnline, (long)(rng.Next(3, 9) * growth));
            }
            utc = utc.AddDays(-1);
            var backups = new List<BackupInfo>
            {
                new BackupInfo { Name = StatsBook.DayOf(utc).ToString("yyyy-MM-dd"), CreatedAt = utc, Accounts = 1284, Bytes = 182_000 },
                new BackupInfo { Name = StatsBook.DayOf(utc.AddDays(-1)).ToString("yyyy-MM-dd"), CreatedAt = utc.AddDays(-1), Accounts = 1251, Bytes = 177_000 }
            };
            var now = new Dashboard.Now { Connected = 23, Rooms = 6, Matches = 4, Humans = 11, Accounts = 1284 };
            System.IO.File.WriteAllText(path, Dashboard.Page(stats, now, backups, null, null), Encoding.UTF8);
        }
    }
}
