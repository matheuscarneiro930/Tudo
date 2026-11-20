using System;

namespace Fodinha.Core
{
    /// <summary>Quantidade de cartas da próxima rodada e o sentido da progressão.</summary>
    public readonly struct RoundPlan
    {
        public readonly int Cards;

        /// <summary>+1 subindo, -1 descendo.</summary>
        public readonly int Direction;

        public RoundPlan(int cards, int direction)
        {
            Cards = cards;
            Direction = direction;
        }
    }

    /// <summary>Decide quantas cartas cada jogador recebe em cada rodada.</summary>
    public static class RoundPlanner
    {
        /// <summary>
        /// Máximo de cartas por jogador com N jogadores ativos.
        /// No modo Vira, 1 carta fica reservada para ser virada.
        /// </summary>
        public static int MaxCardsPerPlayer(int activePlayers, RuleSet rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (activePlayers < 1) throw new ArgumentOutOfRangeException(nameof(activePlayers));

            int reserved = rules.ManilhaMode == ManilhaMode.Vira ? 1 : 0;
            int max = (Card.DeckSize - reserved) / activePlayers;
            if (rules.MaxCardsPerPlayer > 0) max = Math.Min(max, rules.MaxCardsPerPlayer);
            return Math.Max(1, max);
        }

        public static RoundPlan FirstRound() => new RoundPlan(1, +1);

        /// <summary>Plano da próxima rodada a partir da rodada anterior.</summary>
        public static RoundPlan Next(RoundPlan previous, int activePlayers, RuleSet rules)
        {
            int max = MaxCardsPerPlayer(activePlayers, rules);
            int prev = Math.Min(Math.Max(previous.Cards, 1), max);

            if (rules.Progression == CardProgression.UpThenHold)
            {
                return new RoundPlan(Math.Min(prev + 1, max), +1);
            }

            // UpAndDown: 1, 2, ..., máx, máx-1, ..., 1, 2, ...
            if (max == 1) return new RoundPlan(1, +1);

            if (previous.Direction >= 0)
            {
                if (prev < max) return new RoundPlan(prev + 1, +1);
                return new RoundPlan(prev - 1, -1);
            }

            if (prev > 1) return new RoundPlan(prev - 1, -1);
            return new RoundPlan(2, +1);
        }
    }
}
