using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fodinha.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Fodinha.Server.Tests
{
    /// <summary>Números do painel: dia de Brasília, pessoas diferentes, gravação e volta depois de reiniciar.</summary>
    public class StatsTests
    {
        private DateTime utc = new DateTime(2026, 11, 24, 15, 0, 0, DateTimeKind.Utc);

        private StatsBook NewBook() => new StatsBook(() => utc);

        [Test]
        public void Dia_EhOHorarioDeBrasilia()
        {
            Assert.That(StatsBook.DayOf(new DateTime(2026, 11, 25, 2, 59, 0, DateTimeKind.Utc)), Is.EqualTo(new DateOnly(2026, 11, 24)));
            Assert.That(StatsBook.DayOf(new DateTime(2026, 11, 25, 3, 0, 0, DateTimeKind.Utc)), Is.EqualTo(new DateOnly(2026, 11, 25)));
        }

        [Test]
        public void PessoaDiferente_ContaUmaVezPorDia()
        {
            var s = NewBook();
            Assert.That(s.FirstToday(Metric.Players, "p1"), Is.True);
            Assert.That(s.FirstToday(Metric.Players, "p1"), Is.False);
            Assert.That(s.FirstToday(Metric.Players, "p2"), Is.True);
            utc = utc.AddDays(1);
            Assert.That(s.FirstToday(Metric.Players, "p1"), Is.True, "No dia seguinte conta de novo.");
        }

        [Test]
        public void Pico_GuardaOMaior()
        {
            var s = NewBook();
            s.Peak(Metric.PeakOnline, 5);
            s.Peak(Metric.PeakOnline, 3);
            s.Peak(Metric.PeakOnline, 9);
            Assert.That(s.Recent(1)[0][Metric.PeakOnline], Is.EqualTo(9));
        }

        [Test]
        public void IpEmbaralhado_NaoAparece_EMudaPorDia()
        {
            var s = NewBook();
            string a = s.Hash("200.150.10.20");
            Assert.That(a, Does.Not.Contain("200"));
            Assert.That(s.Hash("200.150.10.20"), Is.EqualTo(a));
            Assert.That(s.Hash("200.150.10.21"), Is.Not.EqualTo(a));
            utc = utc.AddDays(1);
            Assert.That(s.Hash("200.150.10.20"), Is.Not.EqualTo(a), "Código de um dia não liga com o do outro.");
        }

        [Test]
        public async Task ServidorReinicia_NumerosEPessoasDoDiaVoltam()
        {
            var store = new MemoryStatsStore();
            var s1 = NewBook();
            s1.Load(await store.LoadAsync(s1.Today));
            s1.Add(Metric.Visits, 3);
            if (s1.FirstToday(Metric.Players, "p1")) s1.Add(Metric.Players);
            await new StatsWriter(s1, store, NullLogger<StatsWriter>.Instance).FlushAsync(CancellationToken.None);
            string hash = s1.Hash("1.2.3.4");

            var s2 = NewBook();
            s2.Load(await store.LoadAsync(s2.Today));
            Assert.That(s2.Recent(1)[0][Metric.Visits], Is.EqualTo(3));
            Assert.That(s2.FirstToday(Metric.Players, "p1"), Is.False, "A mesma pessoa não conta de novo depois de reiniciar.");
            Assert.That(s2.Hash("1.2.3.4"), Is.EqualTo(hash), "O segredo do embaralhamento fica guardado.");
            s2.Add(Metric.Visits);
            await new StatsWriter(s2, store, NullLogger<StatsWriter>.Instance).FlushAsync(CancellationToken.None);
            Assert.That(store.Value(s2.Today, Metric.Visits), Is.EqualTo(4), "Grava somando, não por cima.");
        }

        [Test]
        public async Task PessoasDeOntem_SaoApagadasDoBanco()
        {
            var store = new MemoryStatsStore();
            var s = NewBook();
            s.Load(await store.LoadAsync(s.Today));
            s.FirstToday(Metric.Visitors, "a");
            s.FirstToday(Metric.Visitors, "b");
            var writer = new StatsWriter(s, store, NullLogger<StatsWriter>.Instance);
            await writer.FlushAsync(CancellationToken.None);
            Assert.That(store.UniqueCount, Is.EqualTo(2));
            utc = utc.AddDays(1);
            s.FirstToday(Metric.Visitors, "c");
            await writer.FlushAsync(CancellationToken.None);
            Assert.That(store.UniqueCount, Is.EqualTo(1), "Só o dia atual fica guardado.");
        }

        private sealed class BrokenStore : IStatsStore
        {
            public bool Broken = true;
            public readonly MemoryStatsStore Inner = new MemoryStatsStore();
            public Task<StatsState> LoadAsync(DateOnly today, CancellationToken ct = default) => Inner.LoadAsync(today, ct);

            public Task SaveAsync(StatsChanges changes, CancellationToken ct = default) =>
                Broken ? throw new IOException("banco fora do ar") : Inner.SaveAsync(changes, ct);
        }

        [Test]
        public async Task BancoFalha_NaoPerdeNumeros()
        {
            var store = new BrokenStore();
            var s = NewBook();
            var writer = new StatsWriter(s, store, NullLogger<StatsWriter>.Instance);
            s.Add(Metric.Matches, 2);
            await writer.FlushAsync(CancellationToken.None);
            s.Add(Metric.Matches);
            store.Broken = false;
            await writer.FlushAsync(CancellationToken.None);
            Assert.That(store.Inner.Value(s.Today, Metric.Matches), Is.EqualTo(3));
        }

        [TestCase("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15", false, "iphone")]
        [TestCase("Mozilla/5.0 (Linux; Android 14; SM-A546E) AppleWebKit/537.36 Chrome/120 Mobile", false, "android")]
        [TestCase("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120", false, "pc")]
        [TestCase("WhatsApp/2.23.20.0 A", true, "pc")]
        [TestCase("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", true, "pc")]
        [TestCase("facebookexternalhit/1.1", true, "pc")]
        [TestCase("curl/8.4.0", true, "pc")]
        [TestCase("", true, "pc")]
        public void UserAgent_RoboEAparelho(string ua, bool robot, string device)
        {
            Assert.That(Metric.IsRobot(ua), Is.EqualTo(robot));
            Assert.That(Metric.DeviceOf(ua), Is.EqualTo(device));
        }

        [Test]
        public void Plataforma_DesconhecidaViraOutro()
        {
            Assert.That(Metric.NormalizePlatform("android"), Is.EqualTo("android"));
            Assert.That(Metric.NormalizePlatform("site-iphone"), Is.EqualTo("site-iphone"));
            Assert.That(Metric.NormalizePlatform("<script>"), Is.EqualTo("outro"));
            Assert.That(Metric.NormalizePlatform(null), Is.EqualTo("outro"));
        }

        [Test]
        public void Hello_ContaContaNovaEJogadorPorPlataforma()
        {
            var core = new ServerCore(new ServerOptions { Pacing = PacingSettings.Instant });
            var a = new FakeClient(core, "Ana").Hello(platform: "android");
            new FakeClient(core, "Bia").Hello(platform: "site-iphone");
            new FakeClient(core, "Ana2").Hello(a.Token, platform: "android"); // mesma conta, outra conexão

            var d = core.Stats.Recent(1)[0];
            Assert.That(d[Metric.NewAccounts], Is.EqualTo(2));
            Assert.That(d[Metric.NewAccounts + "_android"], Is.EqualTo(1));
            Assert.That(d[Metric.NewAccounts + "_site-iphone"], Is.EqualTo(1));
            Assert.That(d[Metric.Players], Is.EqualTo(2), "A Ana entrou duas vezes: conta uma.");
            Assert.That(d[Metric.Players + "_android"], Is.EqualTo(1));
            Assert.That(d[Metric.PeakOnline], Is.GreaterThanOrEqualTo(2));
        }
    }

    /// <summary>Cópia de segurança das contas: arquivo, conferência e restauração.</summary>
    public class BackupTests
    {
        private static Account Acc(string id, int coins, string token = null)
        {
            var book = new AccountBook();
            var a = book.Create(id, token ?? "token-" + id, "Jogador " + id);
            a.Coins = coins;
            return a.Snapshot();
        }

        [Test]
        public void Arquivo_IdaEVolta_GuardaTudo()
        {
            var a = Acc("p1", 1234);
            a.Avatar = AvatarSpec.FromSeed("x").Encode();
            a.Owned.Add(ShopCatalog.Items.First(i => i.Price > 0).Id);
            a.NoAds = true;
            a.Games = 7;
            a.Wins = 3;
            var back = BackupFile.Read(BackupFile.Write(new[] { a, Acc("p2", 10) }, DateTime.UtcNow));
            Assert.That(back, Has.Count.EqualTo(2));
            var r = back.First(x => x.Id == "p1");
            Assert.That(r.TokenHash, Is.EqualTo(a.TokenHash));
            Assert.That(r.Coins, Is.EqualTo(1234));
            Assert.That(r.Avatar, Is.EqualTo(a.Avatar));
            Assert.That(r.Owned, Is.EquivalentTo(a.Owned));
            Assert.That(r.NoAds, Is.True);
            Assert.That(r.Games, Is.EqualTo(7));
            Assert.That(r.Wins, Is.EqualTo(3));
        }

        [Test]
        public void Arquivo_NaoTemOTokenDeVerdade()
        {
            var data = BackupFile.Write(new[] { Acc("p1", 5, "segredo-do-aparelho") }, DateTime.UtcNow);
            using var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
            string json = new StreamReader(gz).ReadToEnd();
            Assert.That(json, Does.Not.Contain("segredo-do-aparelho"));
        }

        [Test]
        public void ArquivoInvalido_EhRecusado()
        {
            Assert.Throws<InvalidDataException>(() => BackupFile.Read(Encoding.UTF8.GetBytes("isso não é uma cópia de jeito nenhum")));
            Assert.Throws<InvalidDataException>(() => BackupFile.Read(Gzip("{\"format\":\"outra-coisa\",\"version\":1,\"accounts\":[]}")));
            Assert.Throws<InvalidDataException>(() => BackupFile.Read(Gzip("{\"format\":\"fodinha-contas\",\"version\":99,\"accounts\":[]}")));
            Assert.Throws<InvalidDataException>(() => BackupFile.Read(Gzip(
                "{\"format\":\"fodinha-contas\",\"version\":1,\"accounts\":[{\"id\":\"a\",\"tokenHash\":\"x\"},{\"id\":\"a\",\"tokenHash\":\"y\"}]}")));
            Assert.Throws<InvalidDataException>(() => BackupFile.Read(Gzip(
                "{\"format\":\"fodinha-contas\",\"version\":1,\"accounts\":[{\"id\":\"\",\"tokenHash\":\"x\"}]}")));
        }

        [Test]
        public void Arquivo_LimpaCamposEstranhos()
        {
            var back = BackupFile.Read(Gzip(
                "{\"format\":\"fodinha-contas\",\"version\":1,\"accounts\":[{\"id\":\"a\",\"tokenHash\":\"x\",\"name\":\"<b>Adm\\u202Ein</b>\"," +
                "\"coins\":-50,\"owned\":[\"item_que_nao_existe\"],\"table\":\"mesa_inexistente\"}]}"));
            Assert.That(back[0].Coins, Is.EqualTo(0));
            Assert.That(back[0].Name, Does.Not.Contain("<"));
            // Comparação por caractere: em texto "cultural" o U+202E é ignorado e qualquer texto o "contém".
            Assert.That(back[0].Name.IndexOf('‮'), Is.EqualTo(-1));
            Assert.That(back[0].Owned, Is.Empty);
            Assert.That(back[0].TableTheme, Is.EqualTo(ShopCatalog.DefaultTable));
        }

        private static byte[] Gzip(string text)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Fastest, true))
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                gz.Write(bytes, 0, bytes.Length);
            }
            return ms.ToArray();
        }

        [Test]
        public void Restaurar_VoltaAsContasDaCopia_ENaoMexeNasNovas()
        {
            var book = new AccountBook();
            var ana = book.Create("ana", "t-ana", "Ana");
            var bia = book.Create("bia", "t-bia", "Bia");
            var copy = book.All();

            // Depois da cópia: um "bug" zera a Ana, a Bia gasta, a Carla chega.
            book.AddCoins(ana, -500);
            book.AddCoins(bia, 200);
            var carla = book.Create("carla", "t-carla", "Carla");
            int saved = 0;
            book.Persist = _ => saved++;

            var result = book.Restore(copy);

            Assert.That(result.Updated, Is.EqualTo(2));
            Assert.That(book.FindById("ana").Coins, Is.EqualTo(AccountBook.StartingCoins));
            Assert.That(book.FindById("bia").Coins, Is.EqualTo(AccountBook.StartingCoins));
            Assert.That(book.FindById("carla"), Is.SameAs(carla), "Conta criada depois da cópia continua.");
            Assert.That(book.FindByToken("t-ana"), Is.SameAs(ana), "O mesmo aparelho continua entrando na mesma conta.");
            Assert.That(saved, Is.EqualTo(2), "Tudo que mudou vai para o banco.");
        }

        [Test]
        public void Restaurar_BancoNovoVazio_RecriaAsContas()
        {
            var old = new AccountBook();
            old.Create("ana", "t-ana", "Ana");
            old.AddCoins(old.FindById("ana"), 700);
            var copy = old.All();

            var fresh = new AccountBook();
            var result = fresh.Restore(BackupFile.Read(BackupFile.Write(copy, DateTime.UtcNow)));
            Assert.That(result.Added, Is.EqualTo(1));
            Assert.That(fresh.FindByToken("t-ana").Coins, Is.EqualTo(1200));
        }

        [Test]
        public void Restaurar_TokenDeOutraConta_EhIgnorado()
        {
            var book = new AccountBook();
            book.Create("ana", "t-ana", "Ana");
            var intruder = Acc("intruso", 999999, "t-ana"); // mesmo aparelho da Ana, outra conta
            var result = book.Restore(new[] { intruder });
            Assert.That(result.Skipped, Is.EqualTo(1));
            Assert.That(book.FindByToken("t-ana").Id, Is.EqualTo("ana"));
            Assert.That(book.FindById("intruso"), Is.Null);
        }
    }
}
