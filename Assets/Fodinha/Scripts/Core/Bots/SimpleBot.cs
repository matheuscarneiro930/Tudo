using System;
using System.Collections.Generic;

namespace Fodinha.Core.Bots
{
    /// <summary>
    /// Bot simples e determinístico. Decide SOMENTE a partir do <see cref="MatchView"/> do próprio assento,
    /// ou seja, enxerga exatamente o que um jogador humano enxergaria (não trapaceia).
    /// Usado para: jogadores bot no modo offline, tempo esgotado e jogadores desconectados.
    /// </summary>
    public static class SimpleBot
    {
        public static int ChooseBet(MatchView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (view.AllowedBets.Count == 0) throw new InvalidOperationException("Não é a vez deste assento apostar.");

            double expected = view.IsBlindRound ? EstimateBlindWinChance(view) : EstimateExpectedTricks(view);

            int best = view.AllowedBets[0];
            double bestDistance = double.MaxValue;
            foreach (int bet in view.AllowedBets)
            {
                double distance = Math.Abs(bet - expected);
                if (distance < bestDistance - 1e-9)
                {
                    best = bet;
                    bestDistance = distance;
                }
            }
            return best;
        }

        public static Card ChoosePlay(MatchView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            var hand = view.MyHand;
            if (hand.Count == 0) throw new InvalidOperationException("Mão vazia.");

            var cmp = view.Comparer;
            var me = view.Me;
            int stillNeeded = me.Bet - me.TricksWon;

            if (view.CurrentTrick.Count == 0)
                return stillNeeded > 0 ? Strongest(hand, cmp) : Weakest(hand, cmp);

            var winning = new List<Card>();
            var losing = new List<Card>();
            foreach (var card in hand)
            {
                if (WouldWinNow(view, card)) winning.Add(card);
                else losing.Add(card);
            }

            if (stillNeeded > 0)
                return winning.Count > 0 ? Weakest(winning, cmp) : Weakest(hand, cmp);

            // Já fez o que apostou: tenta perder, se livrando da carta mais alta que ainda perde.
            return losing.Count > 0 ? Strongest(losing, cmp) : Weakest(hand, cmp);
        }

        /// <summary>Soma das probabilidades de cada carta da mão vencer uma vaza contra os adversários.</summary>
        private static double EstimateExpectedTricks(MatchView view)
        {
            var cmp = view.Comparer;
            int opponents = Math.Max(1, view.ActivePlayerCount - 1);

            var known = new HashSet<Card>(view.MyHand);
            if (view.Vira.HasValue) known.Add(view.Vira.Value);

            var unseen = new List<Card>();
            foreach (var c in Deck.AllCards) if (!known.Contains(c)) unseen.Add(c);
            if (unseen.Count == 0) return 0;

            double expected = 0;
            foreach (var card in view.MyHand)
            {
                int strength = cmp.Strength(card);
                int weaker = 0;
                foreach (var u in unseen) if (cmp.Strength(u) < strength) weaker++;
                double beatOne = (double)weaker / unseen.Count;
                expected += Math.Pow(beatOne, opponents);
            }
            return expected;
        }

        /// <summary>Rodada cega: chance da carta desconhecida vencer as cartas visíveis dos outros.</summary>
        private static double EstimateBlindWinChance(MatchView view)
        {
            var cmp = view.Comparer;
            var visiblePlays = new List<PlayedCard>();
            var known = new HashSet<Card>();
            if (view.Vira.HasValue) known.Add(view.Vira.Value);

            foreach (var p in view.Players)
            {
                if (p.Seat == view.ViewerSeat) continue;
                foreach (var c in p.VisibleCards)
                {
                    visiblePlays.Add(new PlayedCard(p.Seat, c));
                    known.Add(c);
                }
            }

            int total = 0, wins = 0;
            foreach (var candidate in Deck.AllCards)
            {
                if (known.Contains(candidate)) continue;
                total++;
                var plays = new List<PlayedCard>(visiblePlays) { new PlayedCard(view.ViewerSeat, candidate) };
                if (cmp.ResolveTrick(plays).WinnerSeat == view.ViewerSeat) wins++;
            }
            return total == 0 ? 0 : (double)wins / total;
        }

        private static bool WouldWinNow(MatchView view, Card card)
        {
            var plays = new List<PlayedCard>(view.CurrentTrick) { new PlayedCard(view.ViewerSeat, card) };
            return view.Comparer.ResolveTrick(plays).WinnerSeat == view.ViewerSeat;
        }

        private static Card Strongest(IReadOnlyList<Card> cards, CardComparer cmp)
        {
            var best = cards[0];
            for (int i = 1; i < cards.Count; i++) if (cmp.Strength(cards[i]) > cmp.Strength(best)) best = cards[i];
            return best;
        }

        private static Card Weakest(IReadOnlyList<Card> cards, CardComparer cmp)
        {
            var best = cards[0];
            for (int i = 1; i < cards.Count; i++) if (cmp.Strength(cards[i]) < cmp.Strength(best)) best = cards[i];
            return best;
        }
    }
}
