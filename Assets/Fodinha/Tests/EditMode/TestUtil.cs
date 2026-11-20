using System.Collections.Generic;
using Fodinha.Core;

namespace Fodinha.Core.Tests
{
    internal static class TestUtil
    {
        public static Card C(string text) => Card.Parse(text);

        public static List<Card> Cards(params string[] texts)
        {
            var list = new List<Card>();
            foreach (var t in texts) list.Add(Card.Parse(t));
            return list;
        }

        /// <summary>Baralho montado: as cartas dadas saem primeiro, na ordem; o resto do baralho vem depois.</summary>
        public static List<Card> Stacked(params string[] firstDrawn)
        {
            var list = Cards(firstDrawn);
            var used = new HashSet<Card>(list);
            foreach (var c in Deck.AllCards) if (!used.Contains(c)) list.Add(c);
            return list;
        }

        public static string[] Names(int count)
        {
            var names = new string[count];
            for (int i = 0; i < count; i++) names[i] = "J" + (i + 1);
            return names;
        }

        public static MatchEngine NewEngine(int players, RuleSet rules = null, int seed = 1)
        {
            return new MatchEngine(rules ?? RuleSet.CreateDefault(), Names(players), new SystemRandomSource(seed));
        }
    }
}
