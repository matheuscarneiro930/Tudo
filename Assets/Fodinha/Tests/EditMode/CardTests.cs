using System.Collections.Generic;
using Fodinha.Core;
using NUnit.Framework;

namespace Fodinha.Core.Tests
{
    public class CardTests
    {
        [Test]
        public void DeckHas40UniqueCards()
        {
            var set = new HashSet<Card>(Deck.AllCards);
            Assert.AreEqual(40, Deck.AllCards.Count);
            Assert.AreEqual(40, set.Count);
        }

        [Test]
        public void IdRoundTripsForAllCards()
        {
            for (int id = 0; id < Card.DeckSize; id++)
                Assert.AreEqual(id, Card.FromId(id).Id);
        }

        [TestCase("4P", Rank.Four, Suit.Clubs)]
        [TestCase("7♥", Rank.Seven, Suit.Hearts)]
        [TestCase("AE", Rank.Ace, Suit.Spades)]
        [TestCase("3o", Rank.Three, Suit.Diamonds)]
        [TestCase("QC", Rank.Queen, Suit.Hearts)]
        public void ParsesCards(string text, Rank rank, Suit suit)
        {
            var card = Card.Parse(text);
            Assert.AreEqual(rank, card.Rank);
            Assert.AreEqual(suit, card.Suit);
        }

        [TestCase("8P")]
        [TestCase("4X")]
        [TestCase("")]
        [TestCase("10P")]
        public void RejectsInvalidText(string text)
        {
            Assert.IsFalse(Card.TryParse(text, out _));
        }

        [Test]
        public void RankSequenceWrapsFromThreeToFour()
        {
            Assert.AreEqual(Rank.Four, Rank.Three.Next());
            Assert.AreEqual(Rank.Six, Rank.Five.Next());
            Assert.AreEqual(Rank.Jack, Rank.Queen.Next());
            Assert.AreEqual(Rank.King, Rank.Jack.Next());
            Assert.AreEqual(Rank.Ace, Rank.King.Next());
            Assert.AreEqual(Rank.Queen, Rank.Seven.Next());
        }

        [Test]
        public void ShuffleKeepsAllCards()
        {
            var deck = new Deck();
            deck.Shuffle(new SystemRandomSource(42));
            var drawn = new HashSet<Card>();
            while (deck.Count > 0) drawn.Add(deck.Draw());
            Assert.AreEqual(40, drawn.Count);
        }
    }
}
