using Fodinha.Core;
using NUnit.Framework;

namespace Fodinha.Core.Tests
{
    public class BetRulesTests
    {
        [Test]
        public void NonLastBidderCanBetZeroToCards()
        {
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, BetRules.AllowedBets(3, false, 2, RuleSet.CreateDefault()));
        }

        [Test]
        public void LastBidderCannotCloseTheSum()
        {
            // 3 cartas, os outros somaram 1 → o pé não pode apostar 2.
            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, BetRules.AllowedBets(3, true, 1, RuleSet.CreateDefault()));
        }

        [Test]
        public void LastBidderUnrestrictedWhenSumAlreadyExceeds()
        {
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, BetRules.AllowedBets(3, true, 5, RuleSet.CreateDefault()));
        }

        [Test]
        public void RestrictionCanBeDisabled()
        {
            var rules = RuleSet.CreateDefault();
            rules.LastBidderRestriction = false;
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, BetRules.AllowedBets(3, true, 1, rules));
        }

        [Test]
        public void LastBidderAlwaysHasAnOption()
        {
            var rules = RuleSet.CreateDefault();
            for (int cards = 1; cards <= 19; cards++)
                for (int sum = 0; sum <= 30; sum++)
                    Assert.IsNotEmpty(BetRules.AllowedBets(cards, true, sum, rules));
        }

        [TestCase(2, 2, LifeLossMode.OnePerMiss, 0)]
        [TestCase(0, 3, LifeLossMode.OnePerMiss, 1)]
        [TestCase(3, 0, LifeLossMode.OnePerMiss, 1)]
        [TestCase(0, 3, LifeLossMode.Difference, 3)]
        [TestCase(2, 1, LifeLossMode.Difference, 1)]
        public void LivesLost(int bet, int won, LifeLossMode mode, int expected)
        {
            var rules = RuleSet.CreateDefault();
            rules.LifeLossMode = mode;
            Assert.AreEqual(expected, BetRules.LivesLost(bet, won, rules));
        }

        [Test]
        public void RuleValidation()
        {
            Assert.IsTrue(RuleSet.CreateDefault().IsValid);
            var rules = RuleSet.CreateDefault();
            rules.StartingLives = 6;
            rules.TurnTimeSeconds = 10;
            Assert.AreEqual(2, rules.GetValidationErrors().Count);
        }
    }
}
