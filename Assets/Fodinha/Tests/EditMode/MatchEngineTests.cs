using System.Collections.Generic;
using Fodinha.Core;
using NUnit.Framework;
using static Fodinha.Core.Tests.TestUtil;

namespace Fodinha.Core.Tests
{
    public class MatchEngineTests
    {
        [Test]
        public void RejectsInvalidPlayerCounts()
        {
            Assert.Throws<System.ArgumentException>(() => NewEngine(1));
            Assert.Throws<System.ArgumentException>(() => NewEngine(16));
            Assert.DoesNotThrow(() => NewEngine(2));
            Assert.DoesNotThrow(() => NewEngine(15));
        }

        [Test]
        public void RejectsInvalidRules()
        {
            var rules = RuleSet.CreateDefault();
            rules.StartingLives = 1;
            Assert.Throws<System.ArgumentException>(() => NewEngine(4, rules));
        }

        [Test]
        public void RandomDealerIsAValidSeat()
        {
            var dealers = new HashSet<int>();
            for (int seed = 0; seed < 50; seed++)
            {
                var engine = NewEngine(5, seed: seed);
                engine.StartMatch();
                Assert.That(engine.DealerSeat, Is.InRange(0, 4));
                dealers.Add(engine.DealerSeat);
            }
            Assert.Greater(dealers.Count, 1, "O distribuidor inicial deveria variar entre partidas.");
        }

        [Test]
        public void FirstRoundIsOneBlindCardAndStartsAfterDealer()
        {
            var engine = NewEngine(3);
            engine.StartMatch(0);
            Assert.AreEqual(MatchPhase.Betting, engine.Phase);
            Assert.AreEqual(1, engine.RoundNumber);
            Assert.AreEqual(1, engine.CardsThisRound);
            Assert.IsTrue(engine.IsBlindRound);
            Assert.IsTrue(engine.Vira.HasValue);
            Assert.AreEqual(1, engine.CurrentSeat);
            foreach (var p in engine.Players)
            {
                Assert.AreEqual(1, p.Hand.Count);
                Assert.AreEqual(3, p.Lives);
            }
        }

        [Test]
        public void BlindRoundViewHidesOwnCardAndShowsOthers()
        {
            var engine = NewEngine(3);
            engine.StackedDecks.Enqueue(Stacked("KC", "4E", "3O", "7O"));
            engine.StartMatch(0);

            var view = engine.BuildView(1);
            Assert.IsEmpty(view.MyHand);
            Assert.IsEmpty(view.Players[1].VisibleCards);
            CollectionAssert.AreEqual(Cards("3O"), view.Players[0].VisibleCards);
            CollectionAssert.AreEqual(Cards("4E"), view.Players[2].VisibleCards);

            var spectator = engine.BuildView(MatchView.Spectator);
            Assert.IsEmpty(spectator.MyHand);
            Assert.AreEqual(1, spectator.Players[1].VisibleCards.Count);
        }

        [Test]
        public void NormalRoundViewShowsOnlyOwnHand()
        {
            var engine = NewEngine(4);
            engine.StartMatch(0);
            PlayOutRound(engine);
            engine.StartNextRound();
            Assert.AreEqual(2, engine.CardsThisRound);
            Assert.IsFalse(engine.IsBlindRound);

            for (int seat = 0; seat < 4; seat++)
            {
                var view = engine.BuildView(seat);
                CollectionAssert.AreEquivalent(engine.Players[seat].Hand, view.MyHand);
                foreach (var pv in view.Players)
                {
                    Assert.IsEmpty(pv.VisibleCards);
                    Assert.AreEqual(2, pv.HandCount);
                }
            }
        }

        [Test]
        public void FullScenario_BettingTurnsTricksAndScoring()
        {
            var engine = NewEngine(3);
            engine.StackedDecks.Enqueue(Stacked("KC", "4E", "3O", "7O"));          // rodada 1: assento1, assento2, assento0, vira
            engine.StackedDecks.Enqueue(Stacked("3P", "2P", "AP", "4P", "5P", "QP", "6O")); // rodada 2
            engine.StartMatch(0);

            // ---- Rodada 1 (cega). Aposta começa no assento 1; o distribuidor (0) é o pé.
            Assert.AreEqual(ActionResult.NotYourTurn, engine.TryPlaceBet(0, 0));
            Assert.AreEqual(ActionResult.BetOutOfRange, engine.TryPlaceBet(1, 2));
            Assert.AreEqual(ActionResult.Ok, engine.TryPlaceBet(1, 1));
            Assert.AreEqual(ActionResult.Ok, engine.TryPlaceBet(2, 0));
            CollectionAssert.AreEqual(new[] { 1 }, engine.GetAllowedBets(0));
            Assert.AreEqual(ActionResult.BetForbiddenForLastBidder, engine.TryPlaceBet(0, 0));
            Assert.AreEqual(ActionResult.Ok, engine.TryPlaceBet(0, 1));

            // Rodada cega revela tudo junto, começando depois do distribuidor.
            Assert.AreEqual(MatchPhase.TrickComplete, engine.Phase);
            Assert.AreEqual(new[] { 1, 2, 0 }, SeatsOf(engine.LastTrick.Plays));
            Assert.AreEqual(0, engine.LastTrick.WinnerSeat);

            engine.ContinueAfterTrick();
            Assert.AreEqual(MatchPhase.RoundComplete, engine.Phase);
            Assert.AreEqual(3, engine.Players[0].Lives);
            Assert.AreEqual(2, engine.Players[1].Lives); // apostou 1, fez 0
            Assert.AreEqual(3, engine.Players[2].Lives);

            // ---- Rodada 2: distribuidor passa para o assento 1, 2 cartas, aposta começa no assento 2.
            engine.StartNextRound();
            Assert.AreEqual(1, engine.DealerSeat);
            Assert.AreEqual(2, engine.CardsThisRound);
            Assert.AreEqual(2, engine.CurrentSeat);
            CollectionAssert.AreEquivalent(Cards("3P", "4P"), engine.Players[2].Hand);
            CollectionAssert.AreEquivalent(Cards("2P", "5P"), engine.Players[0].Hand);
            CollectionAssert.AreEquivalent(Cards("AP", "QP"), engine.Players[1].Hand);

            Assert.AreEqual(ActionResult.WrongPhase, engine.TryPlayCard(2, C("3P")));
            Assert.AreEqual(ActionResult.Ok, engine.TryPlaceBet(2, 1));
            Assert.AreEqual(ActionResult.Ok, engine.TryPlaceBet(0, 0));
            Assert.AreEqual(ActionResult.BetForbiddenForLastBidder, engine.TryPlaceBet(1, 1));
            Assert.AreEqual(ActionResult.Ok, engine.TryPlaceBet(1, 0));

            // Primeira vaza: começa depois do distribuidor; o distribuidor joga por último.
            Assert.AreEqual(MatchPhase.Playing, engine.Phase);
            Assert.AreEqual(2, engine.CurrentSeat);
            Assert.AreEqual(ActionResult.NotYourTurn, engine.TryPlayCard(0, C("2P")));
            Assert.AreEqual(ActionResult.Ok, engine.TryPlayCard(2, C("3P")));
            Assert.AreEqual(ActionResult.CardNotInHand, engine.TryPlayCard(0, C("3P")));
            Assert.AreEqual(ActionResult.Ok, engine.TryPlayCard(0, C("2P")));
            Assert.AreEqual(ActionResult.Ok, engine.TryPlayCard(1, C("AP")));
            Assert.AreEqual(MatchPhase.TrickComplete, engine.Phase);
            Assert.AreEqual(2, engine.LastTrick.WinnerSeat);

            // Quem levou começa a próxima.
            engine.ContinueAfterTrick();
            Assert.AreEqual(2, engine.CurrentSeat);
            Assert.AreEqual(ActionResult.Ok, engine.TryPlayCard(2, C("4P")));
            Assert.AreEqual(ActionResult.Ok, engine.TryPlayCard(0, C("5P")));
            Assert.AreEqual(ActionResult.Ok, engine.TryPlayCard(1, C("QP")));
            Assert.AreEqual(1, engine.LastTrick.WinnerSeat);

            engine.ContinueAfterTrick();
            Assert.AreEqual(MatchPhase.RoundComplete, engine.Phase);
            Assert.AreEqual(3, engine.Players[2].Lives); // apostou 1, fez 1
            Assert.AreEqual(3, engine.Players[0].Lives); // apostou 0, fez 0
            Assert.AreEqual(1, engine.Players[1].Lives); // apostou 0, fez 1

            var summary = engine.LastRoundSummary;
            Assert.AreEqual(2, summary.RoundNumber);
            Assert.AreEqual(3, summary.Results.Count);
        }

        [Test]
        public void WrongPhaseCallsThrowForHostMethods()
        {
            var engine = NewEngine(2);
            Assert.Throws<System.InvalidOperationException>(() => engine.ContinueAfterTrick());
            engine.StartMatch(0);
            Assert.Throws<System.InvalidOperationException>(() => engine.StartMatch(0));
            Assert.Throws<System.InvalidOperationException>(() => engine.StartNextRound());
        }

        [Test]
        public void ForfeitedPlayerIsEliminatedAndSkipped()
        {
            var engine = NewEngine(3);
            engine.StartMatch(0);
            engine.Forfeit(1);
            Assert.IsTrue(engine.Players[1].IsAutoPlay);
            Assert.IsTrue(engine.IsWaitingForBot);
            PlayOutRound(engine);

            Assert.IsTrue(engine.Players[1].IsEliminated);
            Assert.AreEqual(1, engine.Players[1].EliminatedAtRound);

            engine.StartNextRound();
            Assert.AreEqual(2, engine.DealerSeat, "O distribuidor deve pular o eliminado.");
            Assert.AreEqual(0, engine.Players[1].Hand.Count);
            Assert.AreEqual(0, engine.CurrentSeat);
            Assert.AreEqual(ActionResult.PlayerEliminated, engine.TryPlaceBet(1, 0));
        }

        [Test]
        public void TimeoutsTurnOnAutoPlay()
        {
            var engine = NewEngine(2);
            var events = new List<MatchEvent>();
            engine.EventRaised += events.Add;
            engine.StartMatch(0);

            Assert.AreEqual(ActionResult.Ok, engine.HandleTimeout(1));
            Assert.AreEqual(1, engine.Players[1].ConsecutiveTimeouts);
            Assert.IsFalse(engine.Players[1].IsAutoPlay);
            PlayOutRound(engine);
            if (engine.Phase == MatchPhase.MatchOver) Assert.Inconclusive("Partida terminou cedo demais para este cenário.");

            engine.StartNextRound(); // distribuidor 1 → aposta começa no 0
            Assert.AreEqual(ActionResult.Ok, engine.TryPlaceBet(0, engine.GetAllowedBets(0)[0]));
            Assert.AreEqual(ActionResult.Ok, engine.HandleTimeout(1));
            Assert.IsTrue(engine.Players[1].IsAutoPlay);
            Assert.IsTrue(events.Exists(e => e.Type == MatchEventType.AutoPlayChanged && e.Seat == 1));

            engine.SetAutoPlay(1, false);
            Assert.AreEqual(0, engine.Players[1].ConsecutiveTimeouts);
        }

        [Test]
        public void SimultaneousZero_MostLivesBeforeRoundWins()
        {
            var rules = RuleSet.CreateDefault();
            rules.StartingLives = 2;
            rules.LifeLossMode = LifeLossMode.Difference;
            var engine = NewEngine(2, rules);
            engine.StackedDecks.Enqueue(Stacked("4C", "3C", "7O"));
            engine.StackedDecks.Enqueue(Stacked("KC", "KE", "JC", "JE", "4O"));
            engine.StartMatch(0);

            Assert.AreEqual(ActionResult.Ok, engine.TryPlaceBet(1, 0));
            Assert.AreEqual(ActionResult.Ok, engine.TryPlaceBet(0, 0));
            engine.ContinueAfterTrick();
            Assert.AreEqual(1, engine.Players[0].Lives);
            Assert.AreEqual(2, engine.Players[1].Lives);

            engine.StartNextRound();
            Assert.AreEqual(ActionResult.Ok, engine.TryPlaceBet(0, 2));
            Assert.AreEqual(ActionResult.Ok, engine.TryPlaceBet(1, 2));
            Assert.AreEqual(ActionResult.Ok, engine.TryPlayCard(0, C("KC")));
            Assert.AreEqual(ActionResult.Ok, engine.TryPlayCard(1, C("KE")));
            Assert.IsFalse(engine.LastTrick.HasWinner);
            engine.ContinueAfterTrick();
            Assert.AreEqual(0, engine.CurrentSeat, "Sem vencedor, quem começou a vaza anterior começa de novo.");
            Assert.AreEqual(ActionResult.Ok, engine.TryPlayCard(0, C("JC")));
            Assert.AreEqual(ActionResult.Ok, engine.TryPlayCard(1, C("JE")));
            engine.ContinueAfterTrick();

            Assert.AreEqual(MatchPhase.MatchOver, engine.Phase);
            Assert.AreEqual(1, engine.WinnerSeat);
            Assert.AreEqual(1, engine.Players[1].Lives);
            Assert.IsTrue(engine.Players[0].IsEliminated);
            Assert.IsTrue(engine.LastRoundSummary.SimultaneousEliminationTiebreak);
        }

        [Test]
        public void SimultaneousZero_TiedPlayersReturnWithOneLife()
        {
            var rules = RuleSet.CreateDefault();
            rules.StartingLives = 2;
            var engine = NewEngine(2, rules);
            engine.StackedDecks.Enqueue(Stacked("KC", "KE", "4O"));
            engine.StackedDecks.Enqueue(Stacked("KC", "KE", "JC", "JE", "4O"));
            engine.StartMatch(0);

            Assert.AreEqual(ActionResult.Ok, engine.TryPlaceBet(1, 1));
            Assert.AreEqual(ActionResult.Ok, engine.TryPlaceBet(0, 1));
            engine.ContinueAfterTrick();
            Assert.AreEqual(1, engine.Players[0].Lives);
            Assert.AreEqual(1, engine.Players[1].Lives);

            engine.StartNextRound();
            Assert.AreEqual(ActionResult.Ok, engine.TryPlaceBet(0, 1));
            Assert.AreEqual(ActionResult.Ok, engine.TryPlaceBet(1, 2));
            Assert.AreEqual(ActionResult.Ok, engine.TryPlayCard(0, C("KC")));
            Assert.AreEqual(ActionResult.Ok, engine.TryPlayCard(1, C("KE")));
            engine.ContinueAfterTrick();
            Assert.AreEqual(ActionResult.Ok, engine.TryPlayCard(0, C("JC")));
            Assert.AreEqual(ActionResult.Ok, engine.TryPlayCard(1, C("JE")));
            engine.ContinueAfterTrick();

            Assert.AreEqual(MatchPhase.RoundComplete, engine.Phase);
            Assert.IsTrue(engine.LastRoundSummary.SimultaneousEliminationTiebreak);
            Assert.AreEqual(1, engine.Players[0].Lives);
            Assert.AreEqual(1, engine.Players[1].Lives);
            Assert.IsFalse(engine.Players[0].IsEliminated);
            Assert.IsFalse(engine.Players[1].IsEliminated);
        }

        [Test]
        public void EventsNeverRevealHands()
        {
            var engine = NewEngine(4);
            engine.EventRaised += e =>
            {
                if (!e.Card.HasValue) return;
                Assert.That(e.Type == MatchEventType.CardPlayed || e.Type == MatchEventType.ViraRevealed,
                    "Somente cartas jogadas e a vira podem aparecer em eventos públicos.");
            };
            engine.StartMatch(0);
            PlayOutRound(engine);
        }

        // ---------------------------------------------------------------- helpers

        private static int[] SeatsOf(IReadOnlyList<PlayedCard> plays)
        {
            var seats = new int[plays.Count];
            for (int i = 0; i < plays.Count; i++) seats[i] = plays[i].Seat;
            return seats;
        }

        /// <summary>Joga a rodada atual até o fim usando o bot para todos.</summary>
        internal static void PlayOutRound(MatchEngine engine)
        {
            int guard = 0;
            while (engine.Phase != MatchPhase.RoundComplete && engine.Phase != MatchPhase.MatchOver)
            {
                Assert.Less(guard++, 10000, "Rodada não terminou.");
                if (engine.Phase == MatchPhase.TrickComplete) engine.ContinueAfterTrick();
                else Assert.AreEqual(ActionResult.Ok, engine.PerformAutoAction(engine.CurrentSeat));
            }
        }
    }
}
