using System;
using System.Collections.Generic;

namespace Fodinha.Core
{
    /// <summary>Validação de apostas e cálculo de vidas perdidas.</summary>
    public static class BetRules
    {
        /// <summary>
        /// Apostas permitidas: de 0 até o número de cartas da rodada.
        /// Com a regra do pé ativa, o último a apostar não pode fazer a soma das apostas igual ao número de cartas.
        /// </summary>
        public static List<int> AllowedBets(int cardsThisRound, bool isLastBidder, int sumOfPreviousBets, RuleSet rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (cardsThisRound < 1) throw new ArgumentOutOfRangeException(nameof(cardsThisRound));

            var allowed = new List<int>(cardsThisRound + 1);
            int forbidden = isLastBidder && rules.LastBidderRestriction ? cardsThisRound - sumOfPreviousBets : -1;
            for (int bet = 0; bet <= cardsThisRound; bet++)
                if (bet != forbidden) allowed.Add(bet);
            return allowed;
        }

        /// <summary>Vidas perdidas ao fim da rodada.</summary>
        public static int LivesLost(int bet, int tricksWon, RuleSet rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            int diff = Math.Abs(bet - tricksWon);
            if (diff == 0) return 0;
            return rules.LifeLossMode == LifeLossMode.Difference ? diff : 1;
        }
    }
}
