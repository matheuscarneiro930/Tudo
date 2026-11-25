using System.Collections.Generic;
using System.Linq;
using Fodinha.Core;
using Fodinha.Hosting;
using Fodinha.Net;
using NUnit.Framework;

namespace Fodinha.Server.Tests
{
    /// <summary>Mensagens prontas na partida: só número, só para a sala, com limite; bots provocam de vez em quando.</summary>
    public class QuickChatServerTests
    {
        private ServerCore core;

        private void NewCore(bool botChat)
        {
            core = new ServerCore(new ServerOptions { Pacing = PacingSettings.Instant, BotChat = botChat }, new SystemRandomSource(42));
        }

        [SetUp]
        public void SetUp() => NewCore(botChat: false);

        private FakeClient Client(string name, string platform = null) => new FakeClient(core, name).Hello(platform: platform);

        private (FakeClient owner, FakeClient bia) TwoInMatch()
        {
            var owner = Client("Dono");
            owner.Create();
            var bia = Client("Bia");
            bia.Join(owner.Room.code);
            owner.Do(Protocol.ToServer.StartMatch);
            Assert.That(owner.Match, Is.Not.Null, owner.LastError?.message);
            return (owner, bia);
        }

        private static void Say(FakeClient c, int id) => c.Do(Protocol.ToServer.Chat, new ChatMsg { id = id });

        [Test]
        public void Mensagem_ChegaParaTodosDaSala_ComOAssentoDeQuemMandou()
        {
            var (owner, bia) = TwoInMatch();
            var outsider = Client("Fora");
            outsider.Create();

            Say(owner, QuickChat.GoodPlay);

            foreach (var c in new[] { owner, bia })
            {
                Assert.That(c.Chats, Has.Count.EqualTo(1), c.Name + " recebe (quem mandou também, para ver o próprio balão).");
                Assert.That(c.Chats[0].seat, Is.EqualTo(owner.MySeat));
                Assert.That(c.Chats[0].id, Is.EqualTo(QuickChat.GoodPlay));
            }
            Assert.That(outsider.Chats, Is.Empty, "Quem está em outra sala não recebe.");
        }

        [Test]
        public void Mensagem_LevaSoONumero_NuncaTexto()
        {
            var (owner, bia) = TwoInMatch();
            Say(owner, QuickChat.Cry);
            var raw = bia.Inbox.Last(e => e.t == Protocol.ToClient.Chat).d.ToString();
            Assert.That(raw, Does.Not.Contain("chorar"));
            var keys = ((Newtonsoft.Json.Linq.JObject)bia.Inbox.Last(e => e.t == Protocol.ToClient.Chat).d).Properties().Select(p => p.Name);
            Assert.That(keys, Is.EquivalentTo(new[] { "seat", "id" }));
        }

        [TestCase(-1)]
        [TestCase(16)]
        [TestCase(999999)]
        public void NumeroInvalido_EhIgnorado(int id)
        {
            var (owner, bia) = TwoInMatch();
            Say(owner, id);
            owner.Do(Protocol.ToServer.Chat, new { id = "texto livre" });
            owner.Do(Protocol.ToServer.Chat);
            Assert.That(bia.Chats, Is.Empty);
            Assert.That(owner.Closed, Is.False);
        }

        [Test]
        public void Rajada_DeMensagens_SoAPrimeiraPassa_DepoisLibera()
        {
            var (owner, bia) = TwoInMatch();
            for (int i = 0; i < 10; i++) Say(owner, QuickChat.Lol);
            Assert.That(bia.Chats, Has.Count.EqualTo(1), "Intervalo mínimo entre mensagens.");

            core.Tick(QuickChat.MinIntervalSeconds + 0.1f);
            Say(owner, QuickChat.Wow);
            Assert.That(bia.Chats, Has.Count.EqualTo(2));
        }

        [Test]
        public void PorMinuto_TemLimite()
        {
            var (owner, bia) = TwoInMatch();
            for (int i = 0; i < 20; i++)
            {
                Say(owner, QuickChat.Lol);
                core.Tick(QuickChat.MinIntervalSeconds + 0.01f);
            }
            // 20 tentativas em ~51 s: no máximo 8 por minuto.
            Assert.That(bia.Chats.Count, Is.EqualTo(QuickChat.MaxPerMinute));
        }

        [Test]
        public void NoLobby_AntesDaPartida_EhIgnorado()
        {
            var owner = Client("Dono");
            owner.Create();
            var bia = Client("Bia");
            bia.Join(owner.Room.code);
            Say(owner, QuickChat.GoodPlay);
            Assert.That(bia.Chats, Is.Empty);
        }

        [Test]
        public void QuemSaiuDaSala_NaoMandaNemRecebe()
        {
            var (owner, bia) = TwoInMatch();
            var carla = Client("Carla"); // não está na sala
            Say(carla, QuickChat.Cry);
            Assert.That(owner.Chats, Is.Empty);

            bia.Do(Protocol.ToServer.LeaveRoom);
            core.Tick(3f);
            Say(owner, QuickChat.GoodGame);
            Assert.That(bia.Chats, Is.Empty, "Quem saiu não recebe mais.");
        }

        /// <summary>Partida inteira com bots: eles falam às vezes, só pelos próprios assentos e com mensagens válidas.</summary>
        [Test]
        public void Bots_ProvocamDeVezEmQuando_ComIntervalo()
        {
            NewCore(botChat: true);
            var owner = Client("Dono");
            owner.Create();
            for (int i = 0; i < 5; i++) owner.Do(Protocol.ToServer.AddBot);
            owner.Do(Protocol.ToServer.StartMatch);

            var chatTimes = new List<double>();
            int seen = 0;
            for (int i = 0; i < 400000 && owner.Match.view.phase != (int)MatchPhase.MatchOver; i++)
            {
                owner.AutoAct();
                core.Tick(0.05f);
                while (seen < owner.Chats.Count) { chatTimes.Add(core.Now); seen++; }
            }
            Assert.That(owner.Match.view.phase, Is.EqualTo((int)MatchPhase.MatchOver));
            Assert.That(owner.Chats, Is.Not.Empty, "Numa partida inteira os bots falam alguma coisa.");
            foreach (var c in owner.Chats)
            {
                Assert.That(c.seat, Is.Not.EqualTo(owner.MySeat), "O humano não mandou nada: só bots falaram.");
                Assert.That(QuickChat.IsValid(c.id), Is.True);
            }
            for (int i = 1; i < chatTimes.Count; i++)
                Assert.That(chatTimes[i] - chatTimes[i - 1], Is.GreaterThanOrEqualTo(BotBanter.MinGapSeconds - 0.06),
                            "Bots não ficam falando sem parar.");
        }

        [Test]
        public void Bots_RespondemProvocacao()
        {
            NewCore(botChat: true);
            var owner = Client("Dono");
            owner.Create();
            owner.Do(Protocol.ToServer.AddBot);
            owner.Do(Protocol.ToServer.AddBot);
            owner.Do(Protocol.ToServer.StartMatch);

            int replies = 0;
            for (int round = 0; round < 12; round++)
            {
                int before = owner.Chats.Count(c => c.seat != owner.MySeat);
                Say(owner, QuickChat.Scared);
                for (int t = 0; t < 80; t++) core.Tick(0.05f); // 4 s
                if (owner.Chats.Count(c => c.seat != owner.MySeat) > before) replies++;
            }
            Assert.That(replies, Is.GreaterThan(0), "Provocou várias vezes: algum bot responde.");
            Assert.That(replies, Is.LessThan(12), "Mas nem sempre (não vira spam).");
        }

        [Test]
        public void BotChatDesligado_BotsFicamQuietos()
        {
            var owner = Client("Dono");
            owner.Create();
            for (int i = 0; i < 3; i++) owner.Do(Protocol.ToServer.AddBot);
            owner.Do(Protocol.ToServer.StartMatch);
            for (int round = 0; round < 6; round++)
            {
                Say(owner, QuickChat.Scared);
                for (int t = 0; t < 80; t++) { owner.AutoAct(); core.Tick(0.05f); }
            }
            Assert.That(owner.Chats.All(c => c.seat == owner.MySeat), Is.True);
        }

        [Test]
        public void Estatisticas_ContamMensagensEPartidas()
        {
            var (owner, _) = TwoInMatch();
            Say(owner, QuickChat.GoodPlay);
            var today = core.Stats.Recent(1)[0];
            Assert.That(today[Metric.Chats], Is.EqualTo(1));
            Assert.That(today[Metric.Matches], Is.EqualTo(1));
            Assert.That(today[Metric.MatchHumans], Is.EqualTo(2));
        }
    }
}
