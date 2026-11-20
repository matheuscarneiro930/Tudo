using System.Collections.Generic;
using Fodinha.Core;
using NUnit.Framework;

namespace Fodinha.Core.Tests
{
    public class RoundPlannerTests
    {
        [TestCase(2, 19)]
        [TestCase(3, 13)]
        [TestCase(4, 9)]
        [TestCase(5, 7)]
        [TestCase(6, 6)]
        [TestCase(7, 5)]
        [TestCase(8, 4)]
        [TestCase(9, 4)]
        [TestCase(10, 3)]
        [TestCase(12, 3)]
        [TestCase(13, 3)]
        [TestCase(14, 2)]
        [TestCase(15, 2)]
        public void MaxCardsWithVira(int players, int expected)
        {
            Assert.AreEqual(expected, RoundPlanner.MaxCardsPerPlayer(players, RuleSet.CreateDefault()));
        }

        [Test]
        public void MaxCardsNeverExceedsDeck()
        {
            var rules = RuleSet.CreateDefault();
            for (int n = 2; n <= 15; n++)
                Assert.LessOrEqual(n * RoundPlanner.MaxCardsPerPlayer(n, rules) + 1, Card.DeckSize);
        }

        [Test]
        public void FixedManilhasUseWholeDeck()
        {
            var rules = RuleSet.CreateDefault();
            rules.ManilhaMode = ManilhaMode.Fixed;
            Assert.AreEqual(20, RoundPlanner.MaxCardsPerPlayer(2, rules));
            Assert.AreEqual(10, RoundPlanner.MaxCardsPerPlayer(4, rules));
        }

        [Test]
        public void CapLimitsCards()
        {
            var rules = RuleSet.CreateDefault();
            rules.MaxCardsPerPlayer = 5;
            Assert.AreEqual(5, RoundPlanner.MaxCardsPerPlayer(2, rules));
            Assert.AreEqual(2, RoundPlanner.MaxCardsPerPlayer(15, rules));
        }

        private static List<int> Sequence(int players, RuleSet rules, int rounds)
        {
            var list = new List<int>();
            var plan = RoundPlanner.FirstRound();
            list.Add(plan.Cards);
            for (int i = 1; i < rounds; i++)
            {
                plan = RoundPlanner.Next(plan, players, rules);
                list.Add(plan.Cards);
            }
            return list;
        }

        [Test]
        public void UpThenHold_ClimbsAndHolds()
        {
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 4, 4 }, Sequence(8, RuleSet.CreateDefault(), 6));
        }

        [Test]
        public void UpThenHold_ClimbsAgainWhenPlayersLeave()
        {
            var rules = RuleSet.CreateDefault();
            var plan = new RoundPlan(4, +1);
            plan = RoundPlanner.Next(plan, 8, rules);
            Assert.AreEqual(4, plan.Cards);
            plan = RoundPlanner.Next(plan, 7, rules); // 7 jogadores: máximo 5
            Assert.AreEqual(5, plan.Cards);
        }

        [Test]
        public void UpAndDown_GoesUpThenDownThenUp()
        {
            var rules = RuleSet.CreateDefault();
            rules.Progression = CardProgression.UpAndDown;
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 3, 2, 1, 2, 3 }, Sequence(8, rules, 9));
        }

        [Test]
        public void UpAndDown_WithFifteenPlayers()
        {
            var rules = RuleSet.CreateDefault();
            rules.Progression = CardProgression.UpAndDown;
            CollectionAssert.AreEqual(new[] { 1, 2, 1, 2, 1 }, Sequence(15, rules, 5));
        }
    }
}
