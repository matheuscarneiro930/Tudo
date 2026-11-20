using System;
using System.Collections.Generic;

namespace Fodinha.Core
{
    /// <summary>Fonte de números aleatórios. Injetável para permitir partidas reproduzíveis (testes, replays).</summary>
    public interface IRandomSource
    {
        /// <summary>Inteiro em [0, maxExclusive).</summary>
        int Next(int maxExclusive);
    }

    /// <summary>Implementação padrão baseada em System.Random. Só o host/servidor sorteia.</summary>
    public sealed class SystemRandomSource : IRandomSource
    {
        private readonly Random random;

        public SystemRandomSource() { random = new Random(); }
        public SystemRandomSource(int seed) { random = new Random(seed); }

        public int Next(int maxExclusive) => random.Next(maxExclusive);
    }

    /// <summary>Baralho de 40 cartas (sem 8, 9 e 10).</summary>
    public sealed class Deck
    {
        private static readonly Card[] allCards = BuildAllCards();

        private readonly List<Card> cards;

        /// <summary>Todas as 40 cartas, em ordem de Id.</summary>
        public static IReadOnlyList<Card> AllCards => allCards;

        public int Count => cards.Count;

        public Deck()
        {
            cards = new List<Card>(allCards);
        }

        /// <summary>
        /// Baralho "montado" com as cartas na ordem exata em que serão retiradas. Uso exclusivo dos testes.
        /// </summary>
        internal Deck(IReadOnlyList<Card> drawOrder)
        {
            if (drawOrder == null) throw new ArgumentNullException(nameof(drawOrder));
            var seen = new HashSet<Card>();
            foreach (var c in drawOrder)
                if (!seen.Add(c)) throw new ArgumentException("Carta repetida no baralho montado: " + c, nameof(drawOrder));

            cards = new List<Card>(drawOrder.Count);
            for (int i = drawOrder.Count - 1; i >= 0; i--) cards.Add(drawOrder[i]);
        }

        /// <summary>Embaralhamento Fisher-Yates.</summary>
        public void Shuffle(IRandomSource rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            for (int i = cards.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                var tmp = cards[i];
                cards[i] = cards[j];
                cards[j] = tmp;
            }
        }

        /// <summary>Retira a carta do topo.</summary>
        public Card Draw()
        {
            if (cards.Count == 0) throw new InvalidOperationException("O baralho acabou.");
            int last = cards.Count - 1;
            var card = cards[last];
            cards.RemoveAt(last);
            return card;
        }

        private static Card[] BuildAllCards()
        {
            var result = new Card[Card.DeckSize];
            for (int id = 0; id < Card.DeckSize; id++) result[id] = Card.FromId(id);
            return result;
        }
    }
}
