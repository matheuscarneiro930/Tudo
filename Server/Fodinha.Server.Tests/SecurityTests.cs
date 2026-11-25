using System.Collections.Generic;
using System.Linq;
using Fodinha.Core;
using Fodinha.Hosting;
using Fodinha.Net;
using NUnit.Framework;

namespace Fodinha.Server.Tests
{
    /// <summary>
    /// Ataques de um jogador mal-intencionado (cliente modificado mandando o que quiser).
    /// Cada teste descreve o ataque e confere que o servidor resiste.
    /// </summary>
    public class SecurityTests
    {
        private ServerCore core;

        [SetUp]
        public void SetUp()
        {
            core = new ServerCore(new ServerOptions { Pacing = PacingSettings.Instant, VipCode = "Segredo-123" }, new SystemRandomSource(9));
        }

        // ------------------------------------------------------------------ contas e moedas

        /// <summary>Ataque: criar centenas de contas (500 moedas cada) do mesmo IP para juntar moedas e lotar o banco.</summary>
        [Test]
        public void ContasNovas_DoMesmoIp_SaoLimitadas()
        {
            var bots = new List<FakeClient>();
            for (int i = 0; i < 60; i++) bots.Add(new FakeClient(core, "Farm" + i, "6.6.6.6").Hello());
            int created = bots.Count(b => b.Welcome != null);
            Assert.That(created, Is.LessThanOrEqualTo(20), "Do mesmo IP só dá para criar algumas contas por hora.");
            Assert.That(core.Accounts.Count, Is.EqualTo(created));
            Assert.That(bots.Last().LastError?.code, Is.EqualTo(ErrorCodes.TooMany));

            // Quem já tem conta continua entrando normalmente pelo mesmo IP.
            var again = new FakeClient(core, "Farm0", "6.6.6.6").Hello(bots[0].Token);
            Assert.That(again.Welcome, Is.Not.Null);
            // Outro IP não é afetado.
            Assert.That(new FakeClient(core, "Normal", "7.7.7.7").Hello().Welcome, Is.Not.Null);

            // Passada uma hora, libera de novo.
            for (int i = 0; i < 61; i++) core.Tick(60f);
            Assert.That(new FakeClient(core, "Depois", "6.6.6.6").Hello().Welcome, Is.Not.Null);
        }

        /// <summary>Ataque: script que pede o "+100 moedas" sem parar (sem assistir vídeo nenhum).</summary>
        [Test]
        public void Recompensa_TemLimitePorDia()
        {
            var c = new FakeClient(core, "Farmer").Hello();
            int got = 0;
            for (int i = 0; i < 100; i++)
            {
                core.Accounts.AddCoins(core.Accounts.FindByToken(c.Token), -1000); // gasta tudo (sempre abaixo de 500)
                c.Do(Protocol.ToServer.ClaimReward);
                if (c.Profile?.reason == "recompensa" && c.Profile.delta > 0) { got++; c.Profile.reason = null; }
                core.Tick(25f);
            }
            Assert.That(got, Is.LessThanOrEqualTo(12), "No máximo alguns vídeos premiados por dia.");
        }

        /// <summary>Ataque: chutar o código VIP usando várias contas para fugir do bloqueio por conta.</summary>
        [Test]
        public void CodigoVip_ChuteComVariasContas_BloqueiaPeloIp()
        {
            for (int account = 0; account < 6; account++)
            {
                var c = new FakeClient(core, "Chute" + account, "8.8.8.8").Hello();
                for (int i = 0; i < 4; i++) c.Do(Protocol.ToServer.RedeemCode, new CodeMsg { code = "ERRADO" + account + i });
            }
            var lucky = new FakeClient(core, "Acertou", "8.8.8.8").Hello();
            lucky.Do(Protocol.ToServer.RedeemCode, new CodeMsg { code = "segredo-123" });
            Assert.That(lucky.Profile.noAds, Is.False, "Depois de muitos chutes errados do mesmo IP, nem o código certo passa (por um tempo).");

            var owner = new FakeClient(core, "Dono", "1.1.1.1").Hello();
            owner.Do(Protocol.ToServer.RedeemCode, new CodeMsg { code = "segredo-123" });
            Assert.That(owner.Profile.noAds, Is.True, "O dono, em outra rede, ativa normalmente.");
        }

        /// <summary>Ataque: entrar em salas privadas chutando códigos.</summary>
        [Test]
        public void ChuteDeCodigoDeSala_EhLimitado()
        {
            var host = new FakeClient(core, "Dono").Hello();
            host.Create(new RoomOptions { IsPublic = false });
            string code = host.Room.code;

            var guesser = new FakeClient(core, "Chutador").Hello();
            for (int i = 0; i < 25; i++) guesser.Join("ZZZZ" + (char)('A' + i % 20) + (char)('A' + i / 20));
            guesser.Join(code);
            Assert.That(guesser.Room, Is.Null, "Depois de muitos códigos errados, fica bloqueado um tempo.");
            Assert.That(guesser.LastError.code, Is.EqualTo(ErrorCodes.TooMany));

            for (int i = 0; i < 3; i++) core.Tick(30f);
            guesser.Join(code);
            Assert.That(guesser.Room?.code, Is.EqualTo(code), "Passado o tempo, volta ao normal.");
        }

        // ------------------------------------------------------------------ derrubar o servidor

        /// <summary>Ataque: mandar milhares de mensagens por segundo para travar o servidor de todo mundo.</summary>
        [Test]
        public void EnxurradaDeMensagens_EhCortada()
        {
            var c = new FakeClient(core, "Flood").Hello();
            int before = c.Inbox.Count;
            for (int i = 0; i < 2000; i++) c.Do(Protocol.ToServer.GetProfile);
            int answered = c.Inbox.Count(e => e.t == Protocol.ToClient.Profile) - 1;
            Assert.That(answered, Is.LessThan(100), "O servidor ignora o excesso.");
            Assert.That(c.Closed, Is.True, "E derruba quem insiste.");
            Assert.That(c.Inbox.Count - before, Is.LessThan(150));
        }

        /// <summary>Ataque: abrir conexões e nunca se identificar (ocupando o servidor).</summary>
        [Test]
        public void ConexaoQueNaoSeIdentifica_EhFechada()
        {
            var silent = new FakeClient(core, "Mudo");
            core.OnConnected(silent);
            var normal = new FakeClient(core, "Normal").Hello();
            for (int i = 0; i < 20; i++) core.Tick(1f);
            Assert.That(silent.Closed, Is.True);
            Assert.That(normal.Closed, Is.False);
        }

        /// <summary>Ataque: JSON malformado, aninhado demais ou com tipos trocados.</summary>
        [TestCase("{\"t\":\"bet\",\"d\":{\"value\":\"muito\"}}")]
        [TestCase("{\"t\":\"play\",\"d\":{\"card\":99999999999}}")]
        [TestCase("{\"t\":\"create_room\",\"d\":{\"options\":{\"Stake\":-500,\"MaxPlayers\":99}}}")]
        [TestCase("{\"t\":\"buy_item\",\"d\":{\"item\":null}}")]
        [TestCase("{\"t\":123}")]
        [TestCase("nada a ver")]
        public void MensagensMalformadas_NaoQuebramNada(string json)
        {
            var c = new FakeClient(core, "Hacker").Hello();
            int coins = c.Profile.coins;
            Assert.DoesNotThrow(() => core.OnMessage(c, json));
            Assert.That(c.LastError, Is.Not.Null);
            Assert.That(core.Accounts.FindByToken(c.Token).Coins, Is.EqualTo(coins));
        }

        [Test]
        public void JsonAninhadoDemais_NaoQuebra()
        {
            var c = new FakeClient(core, "Hacker").Hello();
            string deep = "{\"t\":\"hello\",\"d\":" + new string('[', 5000) + new string(']', 5000) + "}";
            Assert.DoesNotThrow(() => core.OnMessage(c, deep));
            Assert.That(c.LastError?.code, Is.EqualTo(ErrorCodes.BadMessage));
        }

        // ------------------------------------------------------------------ identidade

        [Test]
        public void TokenNovo_EhLongoEAleatorio()
        {
            var a = new FakeClient(core, "A").Hello();
            var b = new FakeClient(core, "B").Hello();
            Assert.That(a.Token.Length, Is.GreaterThanOrEqualTo(64));
            Assert.That(a.Token, Is.Not.EqualTo(b.Token));
        }

        /// <summary>Ataque: nome com caracteres invisíveis/inversão de texto (se passar por outro) ou ofensivo.</summary>
        [TestCase("‮odaruj", "odaruj")]
        [TestCase("Z​é‍", "Zé")]
        [TestCase("<color=red>Admin</color>", "color=redAdmin")]
        [TestCase("á́́́́́́b", "á́b")]
        [TestCase("Hitler", "Jogador")]
        [TestCase("h1tl3r_88", "Jogador")]
        [TestCase("   ", "Jogador")]
        [TestCase("Zé Pequeno", "Zé Pequeno")]
        [TestCase("Ana   Maria", "Ana Maria")]
        public void Nome_EhLimpo(string typed, string expected)
        {
            Assert.That(ServerCore.SanitizeName(typed), Is.EqualTo(expected));
        }

        // ------------------------------------------------------------------ jogo (já era seguro: confere)

        [Test]
        public void JogadaForaDaVez_OuCartaQueNaoTem_EhRecusada()
        {
            var a = new FakeClient(core, "A").Hello();
            a.Create();
            var b = new FakeClient(core, "B").Hello();
            b.Join(a.Room.code);
            a.Do(Protocol.ToServer.StartMatch);
            for (int i = 0; i < 400 && a.Match.view.phase != (int)MatchPhase.Betting; i++) core.Tick(0.05f);

            var notTurn = a.Match.view.current == a.MySeat ? b : a;
            notTurn.Do(Protocol.ToServer.Bet, new BetMsg { value = 0 });
            Assert.That(notTurn.LastError?.code, Is.EqualTo(ErrorCodes.Action));
            var turn = notTurn == a ? b : a;
            turn.Do(Protocol.ToServer.Bet, new BetMsg { value = 99 });
            Assert.That(turn.LastError?.code, Is.EqualTo(ErrorCodes.Action));
        }

        [Test]
        public void Ranking_NaoMostraTokens()
        {
            var c = new FakeClient(core, "A").Hello();
            c.Do(Protocol.ToServer.GetRanking);
            string raw = c.Inbox.Last(e => e.t == Protocol.ToClient.Ranking).d.ToString();
            Assert.That(raw, Does.Not.Contain(c.Token));
            Assert.That(raw.ToLowerInvariant(), Does.Not.Contain("token"));
        }
    }
}
