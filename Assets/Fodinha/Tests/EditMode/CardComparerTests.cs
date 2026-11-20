using Fodinha.Core;
using NUnit.Framework;
using static Fodinha.Core.Tests.TestUtil;

namespace Fodinha.Core.Tests
{
    public class CardComparerTests
    {
        private static CardComparer ViraComparer(string vira, TieRule tie = TieRule.CancelOut)
        {
            var rules = RuleSet.CreateDefault();
            rules.TieRule = tie;
            return new CardComparer(rules, C(vira));
        }

        [Test]
        public void NormalHierarchyFollowsTruco()
        {
            // Vira 4 → manilha 5; usamos só cartas sem 5 para testar a ordem normal.
            var cmp = ViraComparer("4O");
            string[] weakestToStrongest = { "6P", "7P", "QP", "JP", "KP", "AP", "2P", "3P" };
            for (int i = 1; i < weakestToStrongest.Length; i++)
                Assert.Greater(cmp.Compare(C(weakestToStrongest[i]), C(weakestToStrongest[i - 1])), 0,
                    weakestToStrongest[i] + " deveria vencer " + weakestToStrongest[i - 1]);
        }

        [TestCase("5O", Rank.Six)]
        [TestCase("3P", Rank.Four)]
        [TestCase("KC", Rank.Ace)]
        [TestCase("7E", Rank.Queen)]
        [TestCase("QE", Rank.Jack)]
        public void ManilhaIsNextRankAfterVira(string vira, Rank expected)
        {
            Assert.AreEqual(expected, ViraComparer(vira).ManilhaRank);
        }

        [Test]
        public void WeakestManilhaBeatsThree()
        {
            var cmp = ViraComparer("5O"); // manilha 6
            Assert.IsTrue(cmp.IsManilha(C("6O")));
            Assert.Greater(cmp.Compare(C("6O"), C("3P")), 0);
        }

        [Test]
        public void ManilhaSuitOrderIsClubsHeartsSpadesDiamonds()
        {
            var cmp = ViraComparer("5O"); // manilha 6
            Assert.Greater(cmp.Compare(C("6P"), C("6C")), 0);
            Assert.Greater(cmp.Compare(C("6C"), C("6E")), 0);
            Assert.Greater(cmp.Compare(C("6E"), C("6O")), 0);
        }

        [Test]
        public void SameRankNormalCardsTieUnlessSuitBreaksTie()
        {
            Assert.AreEqual(0, ViraComparer("5O").Compare(C("3P"), C("3O")));
            Assert.Greater(ViraComparer("5O", TieRule.SuitBreaksTie).Compare(C("3P"), C("3O")), 0);
        }

        [Test]
        public void FixedManilhasOrder()
        {
            var rules = RuleSet.CreateDefault();
            rules.ManilhaMode = ManilhaMode.Fixed;
            var cmp = new CardComparer(rules, null);
            Assert.Greater(cmp.Compare(C("4P"), C("7C")), 0);
            Assert.Greater(cmp.Compare(C("7C"), C("AE")), 0);
            Assert.Greater(cmp.Compare(C("AE"), C("7O")), 0);
            Assert.Greater(cmp.Compare(C("7O"), C("3P")), 0);
            Assert.IsFalse(cmp.IsManilha(C("4C")));
            Assert.Less(cmp.Compare(C("4C"), C("5C")), 0);
        }

        [Test]
        public void ViraModeRequiresVira()
        {
            Assert.Throws<System.ArgumentException>(() => new CardComparer(RuleSet.CreateDefault(), null));
        }

        [Test]
        public void HighestCardWinsWithManyPlayers()
        {
            var cmp = ViraComparer("5O"); // manilha 6
            var plays = new[]
            {
                new PlayedCard(0, C("3P")), new PlayedCard(1, C("2P")), new PlayedCard(2, C("6E")),
                new PlayedCard(3, C("AP")), new PlayedCard(4, C("6O")), new PlayedCard(5, C("KC"))
            };
            Assert.AreEqual(2, cmp.ResolveTrick(plays).WinnerSeat);
        }

        [Test]
        public void CancelOut_TiedTopCardsCancelAndNextHighestWins()
        {
            var cmp = ViraComparer("5O");
            var plays = new[] { new PlayedCard(0, C("3P")), new PlayedCard(1, C("3C")), new PlayedCard(2, C("KO")), new PlayedCard(3, C("4E")) };
            var result = cmp.ResolveTrick(plays);
            Assert.AreEqual(2, result.WinnerSeat);
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, result.CanceledSeats);
        }

        [Test]
        public void CancelOut_CascadingTies()
        {
            var cmp = ViraComparer("5O");
            var plays = new[] { new PlayedCard(0, C("3P")), new PlayedCard(1, C("3C")), new PlayedCard(2, C("KO")), new PlayedCard(3, C("KE")), new PlayedCard(4, C("4E")) };
            Assert.AreEqual(4, cmp.ResolveTrick(plays).WinnerSeat);
        }

        [Test]
        public void CancelOut_AllCancelMeansNoWinner()
        {
            var cmp = ViraComparer("5O");
            var plays = new[] { new PlayedCard(0, C("3P")), new PlayedCard(1, C("3C")) };
            Assert.AreEqual(TrickResult.NoWinner, cmp.ResolveTrick(plays).WinnerSeat);
        }

        [Test]
        public void NoWinnerRule_TopTieMeansNobodyWins()
        {
            var cmp = ViraComparer("5O", TieRule.NoWinner);
            var plays = new[] { new PlayedCard(0, C("3P")), new PlayedCard(1, C("3C")), new PlayedCard(2, C("KO")) };
            Assert.AreEqual(TrickResult.NoWinner, cmp.ResolveTrick(plays).WinnerSeat);
        }

        [Test]
        public void FirstPlayedWinsRule()
        {
            var cmp = ViraComparer("5O", TieRule.FirstPlayedWins);
            var plays = new[] { new PlayedCard(3, C("KO")), new PlayedCard(1, C("3C")), new PlayedCard(0, C("3P")) };
            Assert.AreEqual(1, cmp.ResolveTrick(plays).WinnerSeat);
        }

        [Test]
        public void SuitBreaksTieRule()
        {
            var cmp = ViraComparer("5O", TieRule.SuitBreaksTie);
            var plays = new[] { new PlayedCard(0, C("3O")), new PlayedCard(1, C("3P")), new PlayedCard(2, C("3C")) };
            Assert.AreEqual(1, cmp.ResolveTrick(plays).WinnerSeat);
        }
    }
}
