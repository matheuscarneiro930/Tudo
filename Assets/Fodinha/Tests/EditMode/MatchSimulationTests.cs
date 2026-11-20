using System.Collections.Generic;
using Fodinha.Core;
using Fodinha.Core.Bots;
using NUnit.Framework;

namespace Fodinha.Core.Tests
{
    /// <summary>
    /// Simula partidas completas com bots para TODAS as quantidades de jogadores (2 a 15) e várias variações de regra,
    /// verificando as invariantes do jogo a cada passo.
    /// </summary>
    public class MatchSimulationTests
    {
        private const int SeedsPerConfig = 25;
        private const int MaxRounds = 3000;

        public static IEnumerable<TestCaseData> AllPlayerCounts()
        {
            for (int n = RuleSet.MinPlayers; n <= RuleSet.MaxPlayers; n++)
                yield return new TestCaseData(n).SetName("Simulacao_" + n + "_jogadores");
        }

        [TestCaseSource(nameof(AllPlayerCounts))]
        public void DefaultRules(int players)
        {
            for (int seed = 0; seed < SeedsPerConfig; seed++)
                RunAndVerify(players, RuleSet.CreateDefault(), seed);
        }

        [TestCaseSource(nameof(AllPlayerCounts))]
        public void VariantRules(int players)
        {
            var variants = new List<RuleSet>();

            var a = RuleSet.CreateDefault();
            a.Progression = CardProgression.UpAndDown;
            a.LifeLossMode = LifeLossMode.Difference;
            a.StartingLives = 5;
            variants.Add(a);

            var b = RuleSet.CreateDefault();
            b.ManilhaMode = ManilhaMode.Fixed;
            b.TieRule = TieRule.NoWinner;
            b.StartingLives = 2;
            variants.Add(b);

            var c = RuleSet.CreateDefault();
            c.TieRule = TieRule.SuitBreaksTie;
            c.LastBidderRestriction = false;
            c.BlindOneCardRounds = false;
            c.MaxCardsPerPlayer = 5;
            variants.Add(c);

            var d = RuleSet.CreateDefault();
            d.TieRule = TieRule.FirstPlayedWins;
            d.Progression = CardProgression.UpAndDown;
            variants.Add(d);

            foreach (var rules in variants)
                for (int seed = 0; seed < 8; seed++)
                    RunAndVerify(players, rules, 1000 + seed);
        }

        private static void RunAndVerify(int playerCount, RuleSet rules, int seed)
        {
            var names = TestUtil.Names(playerCount);
            var engine = new MatchEngine(rules, names, new SystemRandomSource(seed));
            string context = playerCount + " jogadores, seed " + seed;

            int expectedCards = 1;
            var plan = RoundPlanner.FirstRound();
            int lastCheckedRound = 0;

            engine.StartMatch();

            int steps = 0;
            while (engine.Phase != MatchPhase.MatchOver)
            {
                Assert.Less(++steps, 2_000_000, "Partida travou: " + context);
                Assert.LessOrEqual(engine.RoundNumber, MaxRounds, "Partida longa demais: " + context);

                if (engine.RoundNumber != lastCheckedRound)
                {
                    lastCheckedRound = engine.RoundNumber;
                    if (engine.RoundNumber > 1)
                    {
                        plan = RoundPlanner.Next(plan, engine.ActivePlayerCount, rules);
                        expectedCards = plan.Cards;
                    }
                    VerifyRoundStart(engine, expectedCards, context);
                }

                switch (engine.Phase)
                {
                    case MatchPhase.Betting:
                        VerifyPrivacy(engine, context);
                        {
                            int seat = engine.CurrentSeat;
                            int bet = SimpleBot.ChooseBet(engine.BuildView(seat));
                            Assert.AreEqual(ActionResult.Ok, engine.TryPlaceBet(seat, bet), context);
                        }
                        break;

                    case MatchPhase.Playing:
                        {
                            int seat = engine.CurrentSeat;
                            Assert.IsFalse(engine.Players[seat].IsEliminated, context);
                            var card = SimpleBot.ChoosePlay(engine.BuildView(seat));
                            Assert.AreEqual(ActionResult.Ok, engine.TryPlayCard(seat, card), context);
                        }
                        break;

                    case MatchPhase.TrickComplete:
                        Assert.AreEqual(engine.ActivePlayerCount, engine.LastTrick.Plays.Count, context);
                        engine.ContinueAfterTrick();
                        if (engine.Phase == MatchPhase.RoundComplete || engine.Phase == MatchPhase.MatchOver)
                            VerifyRoundEnd(engine, rules, context);
                        break;

                    case MatchPhase.RoundComplete:
                        engine.StartNextRound();
                        break;

                    default:
                        Assert.Fail("Fase inesperada " + engine.Phase + ": " + context);
                        break;
                }
            }

            int alive = 0;
            foreach (var p in engine.Players) if (!p.IsEliminated) alive++;
            Assert.AreEqual(1, alive, "Deve sobrar exatamente um jogador: " + context);
            Assert.IsFalse(engine.Players[engine.WinnerSeat].IsEliminated, context);
            Assert.Greater(engine.Players[engine.WinnerSeat].Lives, 0, context);
        }

        private static void VerifyRoundStart(MatchEngine engine, int expectedCards, string context)
        {
            Assert.AreEqual(MatchPhase.Betting, engine.Phase, context);
            Assert.AreEqual(expectedCards, engine.CardsThisRound, "Quantidade de cartas da rodada " + engine.RoundNumber + ": " + context);
            Assert.LessOrEqual(engine.CardsThisRound, RoundPlanner.MaxCardsPerPlayer(engine.ActivePlayerCount, engine.Rules), context);
            Assert.IsFalse(engine.Players[engine.DealerSeat].IsEliminated, "Distribuidor eliminado: " + context);
            Assert.AreEqual(engine.NextActiveSeat(engine.DealerSeat), engine.CurrentSeat, "Aposta deve começar depois do distribuidor: " + context);

            var seen = new HashSet<Card>();
            if (engine.Vira.HasValue) Assert.IsTrue(seen.Add(engine.Vira.Value), context);
            foreach (var p in engine.Players)
            {
                Assert.AreEqual(PlayerState.NoBet, p.Bet, context);
                Assert.AreEqual(0, p.TricksWon, context);
                Assert.That(p.Lives, Is.InRange(0, engine.Rules.StartingLives), context);
                if (p.IsEliminated)
                {
                    Assert.AreEqual(0, p.Hand.Count, "Eliminado recebeu cartas: " + context);
                    continue;
                }
                Assert.Greater(p.Lives, 0, context);
                Assert.AreEqual(engine.CardsThisRound, p.Hand.Count, context);
                foreach (var c in p.Hand) Assert.IsTrue(seen.Add(c), "Carta repetida " + c + ": " + context);
            }
            Assert.LessOrEqual(seen.Count, Card.DeckSize, context);
        }

        private static void VerifyRoundEnd(MatchEngine engine, RuleSet rules, string context)
        {
            var summary = engine.LastRoundSummary;
            Assert.AreEqual(engine.RoundNumber, summary.RoundNumber, context);
            Assert.AreEqual(summary.Cards, engine.RoundTricks.Count, "Número de vazas = número de cartas: " + context);

            int tricksWithWinner = 0;
            foreach (var t in engine.RoundTricks) if (t.HasWinner) tricksWithWinner++;

            int sumWon = 0, sumBets = 0;
            foreach (var r in summary.Results)
            {
                sumWon += r.TricksWon;
                sumBets += r.Bet;
                Assert.That(r.Bet, Is.InRange(0, summary.Cards), context);
                if (!summary.SimultaneousEliminationTiebreak)
                    Assert.AreEqual(r.Hit ? 0 : BetRules.LivesLost(r.Bet, r.TricksWon, rules), r.LivesLost, context);
            }
            Assert.AreEqual(tricksWithWinner, sumWon, context);
            if (rules.LastBidderRestriction) Assert.AreNotEqual(summary.Cards, sumBets, "Regra do pé violada: " + context);
        }

        private static void VerifyPrivacy(MatchEngine engine, string context)
        {
            int viewer = engine.CurrentSeat;
            var view = engine.BuildView(viewer);
            if (engine.IsBlindRound)
            {
                Assert.IsEmpty(view.MyHand, "Rodada cega não pode mostrar a própria carta: " + context);
                Assert.IsEmpty(view.Players[viewer].VisibleCards, context);
            }
            else
            {
                Assert.AreEqual(engine.CardsThisRound, view.MyHand.Count, context);
                foreach (var pv in view.Players) Assert.IsEmpty(pv.VisibleCards, "Mão de outro jogador vazou: " + context);
            }
        }
    }
}
