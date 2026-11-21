using System;
using System.Collections.Generic;
using Fodinha.Core;
using NUnit.Framework;

namespace Fodinha.Hosting.Tests
{
    public class MatchDirectorTests
    {
        private static MatchDirector Create(int players, int humanSeats, PacingSettings pacing, int seed, RuleSet rules = null)
        {
            var names = new List<string>();
            for (int i = 0; i < players; i++) names.Add("J" + i);
            var engine = new MatchEngine(rules ?? RuleSet.CreateDefault(), names, new SystemRandomSource(seed));
            var director = new MatchDirector(engine, pacing, new SystemRandomSource(seed + 1));
            for (int seat = humanSeats; seat < players; seat++) director.SetBotSeat(seat, true);
            return director;
        }

        private static int RunUntil(MatchDirector d, Func<bool> done, float dt, int maxTicks)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                if (done()) return i;
                d.Tick(dt);
            }
            Assert.Fail("A condição não foi atingida em " + maxTicks + " ticks (fase " + d.Engine.Phase + ").");
            return maxTicks;
        }

        [Test]
        public void SoBots_PartidaTermina_De2a15Jogadores([Range(2, 15)] int players)
        {
            var d = Create(players, 0, PacingSettings.Instant, 1000 + players);
            d.Start();
            RunUntil(d, () => d.Engine.Phase == MatchPhase.MatchOver, 0.016f, 200000);
            Assert.That(d.Engine.WinnerSeat, Is.InRange(0, players - 1));
            Assert.That(d.Engine.ActivePlayerCount, Is.EqualTo(1));
        }

        [Test]
        public void JogadorHumanoAusente_TempoEsgota_BotAssume_PartidaTermina()
        {
            var rules = RuleSet.CreateDefault();
            rules.TurnTimeSeconds = 20;
            var d = Create(4, 1, PacingSettings.Instant, 7, rules);
            d.Start();
            RunUntil(d, () => d.Engine.Phase == MatchPhase.MatchOver, 1f, 500000);
            Assert.That(d.Engine.Players[0].ConsecutiveTimeouts >= rules.TimeoutsBeforeAutoPlay || d.Engine.Players[0].IsAutoPlay
                        || d.Engine.Players[0].IsEliminated, Is.True);
        }

        [Test]
        public void TempoEsgotado_ContaTimeout_EAgePeloJogador()
        {
            var rules = RuleSet.CreateDefault();
            rules.BetTimeSeconds = 20; // o primeiro a agir está apostando
            var d = Create(2, 1, PacingSettings.Instant, 3, rules);
            d.Start(firstDealerSeat: 1); // assento 0 aposta primeiro
            Assert.That(d.Engine.CurrentSeat, Is.EqualTo(0));

            d.Tick(0f); // arma o relógio
            Assert.That(d.TimerSeat, Is.EqualTo(0));
            for (int i = 0; i < 19; i++) d.Tick(1f);
            Assert.That(d.Engine.Players[0].HasBet, Is.False, "Ainda havia 1 segundo.");
            d.Tick(1.01f);
            Assert.That(d.Engine.Players[0].HasBet, Is.True, "O bot deveria ter apostado no lugar do jogador.");
            Assert.That(d.Engine.Players[0].ConsecutiveTimeouts, Is.EqualTo(1));
        }

        [Test]
        public void RelogioNaoCorre_DurantePausaDeDistribuicao()
        {
            var pacing = PacingSettings.Instant;
            pacing.DealSeconds = 2f;
            var rules = RuleSet.CreateDefault();
            rules.BetTimeSeconds = 20; // o primeiro a agir está apostando
            var d = Create(2, 1, pacing, 5, rules);
            d.Start(firstDealerSeat: 1);

            d.Tick(1.5f);
            Assert.That(d.TurnTimeRemaining, Is.EqualTo(20f).Within(0.001f));
            d.Tick(1f);
            Assert.That(d.TurnTimeRemaining, Is.EqualTo(20f).Within(0.001f));
            d.Tick(5f);
            Assert.That(d.TurnTimeRemaining, Is.EqualTo(15f).Within(0.001f));
        }

        [Test]
        public void ApostaUsaTempoDeAposta_JogadaUsaTempoDeJogada()
        {
            var rules = RuleSet.CreateDefault();
            rules.BetTimeSeconds = 30;
            rules.TurnTimeSeconds = 50;
            rules.BlindOneCardRounds = false; // a 1ª rodada não é cega, para o humano jogar a carta
            var d = Create(2, 1, PacingSettings.Instant, 21, rules);
            d.Start(firstDealerSeat: 1);
            d.Tick(0f);
            Assert.That(d.TurnTimeTotal, Is.EqualTo(30f));
            Assert.That(d.TurnTimeRemaining, Is.EqualTo(30f).Within(0.01f));

            d.SubmitBet(0, d.Engine.GetAllowedBets(0)[0]);
            RunUntil(d, () => d.Engine.Phase == MatchPhase.Playing && d.Engine.CurrentSeat == 0, 0.01f, 10000);
            d.Tick(0f);
            Assert.That(d.TurnTimeTotal, Is.EqualTo(50f));
            Assert.That(d.TurnTimeRemaining, Is.EqualTo(50f).Within(0.01f));
        }

        [Test]
        public void BotEsperaTempoDePensar()
        {
            var pacing = PacingSettings.Instant;
            pacing.BotMinSeconds = 1f;
            pacing.BotMaxSeconds = 1f;
            var d = Create(2, 0, pacing, 9);
            d.Start(firstDealerSeat: 1);

            d.Tick(0.5f);
            Assert.That(d.Engine.Players[0].HasBet, Is.False);
            d.Tick(0.6f);
            Assert.That(d.Engine.Players[0].HasBet, Is.True);
        }

        [Test]
        public void PausaDepoisDaVaza_RespeitaTempo()
        {
            var pacing = PacingSettings.Instant;
            pacing.TrickResultSeconds = 2f;
            var d = Create(3, 0, pacing, 11);
            d.Start();
            RunUntil(d, () => d.Engine.Phase == MatchPhase.TrickComplete, 0.01f, 10000);

            d.Tick(1f);
            Assert.That(d.Engine.Phase, Is.EqualTo(MatchPhase.TrickComplete));
            Assert.That(d.PhaseWaitRemaining, Is.GreaterThan(0f));
            d.Tick(1.5f);
            Assert.That(d.Engine.Phase, Is.Not.EqualTo(MatchPhase.TrickComplete));
        }

        [Test]
        public void PedidoDeAssentoBot_EhRecusado_PedidoHumanoValidoEhAceito()
        {
            var d = Create(2, 1, PacingSettings.Instant, 13);
            d.Start(firstDealerSeat: 1);

            Assert.That(d.SubmitBet(1, 0), Is.EqualTo(ActionResult.NotYourTurn));
            int bet = d.Engine.GetAllowedBets(0)[0];
            Assert.That(d.SubmitBet(0, bet), Is.EqualTo(ActionResult.Ok));
            Assert.That(d.SubmitBet(0, bet), Is.Not.EqualTo(ActionResult.Ok), "Não pode apostar duas vezes.");
        }

        [Test]
        public void VoltarAJogar_DesligaAutoPlay_MasNaoParaBotDeVerdade()
        {
            var d = Create(3, 1, PacingSettings.Instant, 17);
            d.Start();
            d.Engine.SetAutoPlay(0, true);
            Assert.That(d.IsAutomated(0), Is.True);
            d.RequestResume(0);
            Assert.That(d.IsAutomated(0), Is.False);

            d.RequestResume(1);
            Assert.That(d.IsAutomated(1), Is.True);
        }

        [Test]
        public void JogadorDesconectado_EhControladoPeloBot_SemRelogio()
        {
            var d = Create(2, 2, PacingSettings.Instant, 19);
            d.Start(firstDealerSeat: 1);
            d.Engine.SetConnected(0, false);
            d.Tick(0f);
            Assert.That(d.IsAutomated(0), Is.True);
            Assert.That(d.TimerSeat, Is.EqualTo(-1));
            Assert.That(d.Engine.Players[0].HasBet, Is.True, "Bot instantâneo deveria ter apostado pelo desconectado.");
        }

        [Test]
        public void Version_MudaACadaEvento()
        {
            var d = Create(2, 0, PacingSettings.Instant, 23);
            int v0 = d.Version;
            int events = 0;
            d.EventRaised += _ => events++;
            d.Start();
            Assert.That(d.Version - v0, Is.EqualTo(events));
            Assert.That(events, Is.GreaterThan(0));
        }
    }
}
