using System.Collections.Generic;
using System.Linq;
using Fodinha.Core;
using NUnit.Framework;

namespace Fodinha.Hosting.Tests
{
    /// <summary>Mensagens prontas: lista fixa, limite por jogador e as provocações dos bots.</summary>
    public class QuickChatTests
    {
        [Test]
        public void Lista_TemAsMensagens_CurtasESemRepetir()
        {
            Assert.That(QuickChat.Count, Is.EqualTo(16));
            var texts = Enumerable.Range(0, QuickChat.Count).Select(QuickChat.Text).ToList();
            Assert.That(texts, Is.Unique);
            foreach (var t in texts)
            {
                Assert.That(t.Length, Is.InRange(3, 24), t + ": cabe no balão e no botão.");
                Assert.That(t.Any(c => c > 0x2FFF), Is.False, t + ": sem emoji (a fonte do jogo não desenha).");
            }
        }

        [Test]
        public void Numeros_NuncaMudam()
        {
            // O número vai pela rede: apps antigos e novos precisam concordar. Só se acrescenta no fim.
            Assert.That(QuickChat.Text(QuickChat.GoodPlay), Is.EqualTo("Boa jogada!"));
            Assert.That(QuickChat.Text(QuickChat.GotMe), Is.EqualTo("Você me pegou!"));
            Assert.That(QuickChat.Text(QuickChat.GoodGame), Is.EqualTo("Boa partida!"));
            Assert.That(QuickChat.Text(QuickChat.Cry), Is.EqualTo("Pode chorar!"));
            Assert.That(QuickChat.Text(QuickChat.Ouch), Is.EqualTo("Essa doeu..."));
            Assert.That(QuickChat.KindOf(QuickChat.Thanks), Is.EqualTo(QuickChatKind.Friendly));
            Assert.That(QuickChat.KindOf(QuickChat.Scared), Is.EqualTo(QuickChatKind.Taunt));
            Assert.That(QuickChat.KindOf(QuickChat.Lol), Is.EqualTo(QuickChatKind.Reaction));
        }

        [TestCase(-1)]
        [TestCase(16)]
        [TestCase(int.MaxValue)]
        public void NumeroInvalido(int id)
        {
            Assert.That(QuickChat.IsValid(id), Is.False);
            Assert.That(QuickChat.Text(id), Is.EqualTo(""));
        }

        [Test]
        public void Limite_IntervaloMinimo_EPorMinuto()
        {
            var gate = new QuickChatGate();
            Assert.That(gate.TryAccept(0), Is.True);
            Assert.That(gate.TryAccept(1), Is.False);
            Assert.That(gate.Wait(1), Is.EqualTo(QuickChat.MinIntervalSeconds - 1).Within(1e-9));
            Assert.That(gate.TryAccept(QuickChat.MinIntervalSeconds), Is.True);

            double t = QuickChat.MinIntervalSeconds;
            int accepted = 2;
            for (int i = 0; i < 20; i++) // até ~52 s: dentro do mesmo minuto
            {
                t += QuickChat.MinIntervalSeconds;
                if (gate.TryAccept(t)) accepted++;
            }
            Assert.That(accepted, Is.EqualTo(QuickChat.MaxPerMinute), "No máximo 8 por minuto.");
            Assert.That(gate.TryAccept(t + 61), Is.True, "Passado o minuto, libera.");
        }

        [Test]
        public void Limite_ComFolgaDeRede_AceitaUmPoucoAntes()
        {
            var gate = new QuickChatGate(QuickChatGate.NetworkTolerance);
            Assert.That(gate.TryAccept(10), Is.True);
            Assert.That(gate.TryAccept(10 + QuickChat.MinIntervalSeconds - 0.5), Is.True, "Mensagem que a rede atrasou não é perdida.");
        }

        // ------------------------------------------------------------------ bots

        private sealed class FixedRandom : IRandomSource
        {
            private readonly int value;
            public FixedRandom(int value) { this.value = value; }
            public int Next(int maxExclusive) => System.Math.Min(value, maxExclusive - 1);
        }

        private static (MatchEngine engine, BotBanter banter, List<(int seat, int id)> said) Setup(int roll, int players = 4)
        {
            var engine = new MatchEngine(RuleSet.CreateDefault(), Enumerable.Range(1, players).Select(i => "J" + i).ToList(), new SystemRandomSource(1));
            var banter = new BotBanter(engine, seat => seat != 0, new FixedRandom(roll)); // assento 0 = humano
            var said = new List<(int, int)>();
            banter.Spoke += (s, id) => said.Add((s, id));
            return (engine, banter, said);
        }

        [Test]
        public void Bot_RespondeProvocacao_DepoisDeUmTempinho()
        {
            var (_, banter, said) = Setup(roll: 0); // sorte sempre "sim"
            banter.OnPlayerChat(0, QuickChat.Scared);
            Assert.That(banter.HasPending, Is.True);
            banter.Tick(0.5f);
            Assert.That(said, Is.Empty, "Não responde na mesma hora (parece gente).");
            banter.Tick(2f);
            Assert.That(said, Has.Count.EqualTo(1));
            Assert.That(said[0].seat, Is.Not.EqualTo(0), "Quem responde é um bot.");
            Assert.That(QuickChat.KindOf(said[0].id), Is.Not.EqualTo(QuickChatKind.Friendly));
        }

        [Test]
        public void Bot_AgradeceElogio_ERespondeBoaPartida()
        {
            var (_, banter, said) = Setup(roll: 0);
            banter.OnPlayerChat(0, QuickChat.GoodPlay);
            banter.Tick(3f);
            Assert.That(said.Last().id, Is.EqualTo(QuickChat.Thanks));

            banter.Tick(QuickChat.MinIntervalSeconds + 3f);
            banter.OnEvent(new MatchEvent(MatchEventType.MatchEnded, 1));
            banter.OnPlayerChat(0, QuickChat.GoodGame);
            banter.Tick(3f);
            Assert.That(said.Last().id, Is.EqualTo(QuickChat.GoodGame), "Mesmo depois do fim, responde \"Boa partida!\".");
        }

        [Test]
        public void SorteContra_BotFicaQuieto()
        {
            var (_, banter, said) = Setup(roll: 99);
            banter.OnPlayerChat(0, QuickChat.Scared);
            banter.OnEvent(new MatchEvent(MatchEventType.TrickResolved, 1, 1));
            banter.OnEvent(new MatchEvent(MatchEventType.LifeLost, 0, 1));
            banter.Tick(10f);
            Assert.That(said, Is.Empty);
        }

        [Test]
        public void Bot_ProvocaQuandoOHumanoPerdeVida_EComemoraRodada()
        {
            var (_, banter, said) = Setup(roll: 0);
            banter.OnEvent(new MatchEvent(MatchEventType.LifeLost, 0, 1));
            banter.Tick(3f);
            Assert.That(said, Has.Count.EqualTo(1));
            Assert.That(said[0].seat, Is.Not.EqualTo(0));

            banter.Tick((float)BotBanter.MinGapSeconds);
            banter.OnEvent(new MatchEvent(MatchEventType.TrickResolved, 2, 1));
            banter.Tick(3f);
            Assert.That(said.Last().seat, Is.EqualTo(2), "Quem levou a rodada comemora.");
        }

        [Test]
        public void Bots_NaoFalamSemParar()
        {
            var (_, banter, said) = Setup(roll: 0);
            for (int i = 0; i < 50; i++)
            {
                banter.OnEvent(new MatchEvent(MatchEventType.LifeLost, 0, 1));
                banter.OnEvent(new MatchEvent(MatchEventType.TrickResolved, 1, 1));
                banter.Tick(0.5f);
            }
            // 25 s de partida: no máximo uma fala a cada MinGapSeconds.
            Assert.That(said.Count, Is.LessThanOrEqualTo((int)(25 / BotBanter.MinGapSeconds) + 1));
        }

        [Test]
        public void HumanoNaoFalaPeloBanter_ESemBotsNinguemFala()
        {
            var engine = new MatchEngine(RuleSet.CreateDefault(), new[] { "A", "B" }, new SystemRandomSource(1));
            var banter = new BotBanter(engine, _ => false, new FixedRandom(0)); // todos humanos
            int said = 0;
            banter.Spoke += (s, id) => said++;
            banter.OnPlayerChat(0, QuickChat.Scared);
            banter.OnEvent(new MatchEvent(MatchEventType.LifeLost, 1, 1));
            banter.Tick(10f);
            Assert.That(said, Is.EqualTo(0));
        }

        [Test]
        public void Desligado_FicaQuieto()
        {
            var (_, banter, said) = Setup(roll: 0);
            banter.Enabled = false;
            banter.OnPlayerChat(0, QuickChat.Scared);
            banter.Tick(5f);
            Assert.That(said, Is.Empty);
        }

        [Test]
        public void FimDaPartida_CancelaFalaAgendada()
        {
            var (_, banter, said) = Setup(roll: 0);
            banter.OnEvent(new MatchEvent(MatchEventType.LifeLost, 0, 1));
            banter.OnEvent(new MatchEvent(MatchEventType.MatchEnded, 1));
            banter.Tick(5f);
            Assert.That(said, Is.Empty, "A tela de fim cobre a mesa: provocação atrasada não aparece.");
        }
    }
}
