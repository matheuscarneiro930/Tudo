using System;
using System.Collections.Generic;

namespace Fodinha.Core
{
    /// <summary>Resultado de uma vaza (cada "disputa" de cartas dentro de uma rodada).</summary>
    [Serializable]
    public sealed class TrickResult
    {
        public const int NoWinner = -1;

        /// <summary>Assento que levou a vaza, ou <see cref="NoWinner"/> se ninguém levou (empate).</summary>
        public int WinnerSeat { get; }

        /// <summary>Cartas na ordem em que foram jogadas.</summary>
        public IReadOnlyList<PlayedCard> Plays { get; }

        /// <summary>Assentos cujas cartas foram anuladas por empate.</summary>
        public IReadOnlyList<int> CanceledSeats { get; }

        public bool HasWinner => WinnerSeat != NoWinner;

        public TrickResult(int winnerSeat, IReadOnlyList<PlayedCard> plays, IReadOnlyList<int> canceledSeats)
        {
            WinnerSeat = winnerSeat;
            Plays = plays;
            CanceledSeats = canceledSeats;
        }
    }

    /// <summary>
    /// Compara cartas e decide quem leva cada vaza, para qualquer número de jogadores (2 a 15).
    /// Uma instância vale para uma rodada (a manilha muda a cada rodada).
    /// Só contém informação pública (a vira), então pode ser usado também pelos clientes e bots.
    /// </summary>
    public sealed class CardComparer
    {
        private const int ManilhaBase = 100;

        private static readonly Card[] FixedManilhasWeakestFirst =
        {
            new Card(Rank.Seven, Suit.Diamonds), // pica-fumo
            new Card(Rank.Ace, Suit.Spades),     // espadilha
            new Card(Rank.Seven, Suit.Hearts),   // sete de copas
            new Card(Rank.Four, Suit.Clubs)      // zap
        };

        public ManilhaMode Mode { get; }
        public TieRule TieRule { get; }

        /// <summary>Carta virada (apenas no modo Vira).</summary>
        public Card? Vira { get; }

        /// <summary>Valor que é manilha nesta rodada (apenas no modo Vira).</summary>
        public Rank? ManilhaRank { get; }

        public CardComparer(RuleSet rules, Card? vira)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            Mode = rules.ManilhaMode;
            TieRule = rules.TieRule;

            if (Mode == ManilhaMode.Vira)
            {
                if (!vira.HasValue) throw new ArgumentException("O modo Vira exige uma carta virada.", nameof(vira));
                Vira = vira;
                ManilhaRank = ManilhaFromVira(vira.Value);
            }
        }

        /// <summary>A manilha é o valor seguinte ao da vira (vira 3 → manilha 4).</summary>
        public static Rank ManilhaFromVira(Card vira) => vira.Rank.Next();

        public bool IsManilha(Card card)
        {
            if (Mode == ManilhaMode.Vira) return card.Rank == ManilhaRank;
            return Array.IndexOf(FixedManilhasWeakestFirst, card) >= 0;
        }

        /// <summary>
        /// Força absoluta da carta nesta rodada. Maior = mais forte.
        /// Duas cartas com a mesma força estão empatadas.
        /// </summary>
        public int Strength(Card card)
        {
            if (Mode == ManilhaMode.Vira)
            {
                if (card.Rank == ManilhaRank) return ManilhaBase + (int)card.Suit;
            }
            else
            {
                int fixedIndex = Array.IndexOf(FixedManilhasWeakestFirst, card);
                if (fixedIndex >= 0) return ManilhaBase + fixedIndex;
            }

            int rankStrength = (int)card.Rank * SuitExtensions.SuitCount;
            return TieRule == TieRule.SuitBreaksTie ? rankStrength + (int)card.Suit : rankStrength;
        }

        /// <summary>&gt; 0 se a carta A é mais forte, &lt; 0 se B é mais forte, 0 se empatam.</summary>
        public int Compare(Card a, Card b) => Strength(a).CompareTo(Strength(b));

        /// <summary>
        /// Decide quem leva a vaza. As jogadas devem estar na ordem em que foram feitas.
        /// </summary>
        public TrickResult ResolveTrick(IReadOnlyList<PlayedCard> plays)
        {
            if (plays == null || plays.Count == 0) throw new ArgumentException("Uma vaza precisa de pelo menos uma carta.", nameof(plays));

            var playsCopy = new List<PlayedCard>(plays);
            var canceled = new List<int>();

            // Agrupa as jogadas por força, da mais forte para a mais fraca, mantendo a ordem de jogada dentro do grupo.
            var groups = new SortedDictionary<int, List<PlayedCard>>(Comparer<int>.Create((x, y) => y.CompareTo(x)));
            foreach (var play in playsCopy)
            {
                int strength = Strength(play.Card);
                if (!groups.TryGetValue(strength, out var group))
                {
                    group = new List<PlayedCard>();
                    groups.Add(strength, group);
                }
                group.Add(play);
            }

            int winner = TrickResult.NoWinner;

            switch (TieRule)
            {
                case TieRule.CancelOut:
                    foreach (var group in groups.Values)
                    {
                        if (group.Count == 1)
                        {
                            winner = group[0].Seat;
                            break;
                        }
                        foreach (var p in group) canceled.Add(p.Seat);
                    }
                    break;

                case TieRule.NoWinner:
                    foreach (var group in groups.Values)
                    {
                        if (group.Count == 1) winner = group[0].Seat;
                        else foreach (var p in group) canceled.Add(p.Seat);
                        break; // só o grupo mais forte importa
                    }
                    break;

                case TieRule.FirstPlayedWins:
                case TieRule.SuitBreaksTie:
                    foreach (var group in groups.Values)
                    {
                        winner = group[0].Seat; // primeiro a jogar dentro do grupo mais forte
                        break;
                    }
                    break;

                default:
                    throw new InvalidOperationException("Regra de empate desconhecida: " + TieRule);
            }

            return new TrickResult(winner, playsCopy, canceled);
        }

        /// <summary>
        /// Maior força entre as cartas que ainda "valem" numa vaza em andamento
        /// (considerando anulações por empate). Retorna -1 se nenhuma carta vale no momento.
        /// Útil para bots e para dicas na interface.
        /// </summary>
        public int CurrentWinningStrength(IReadOnlyList<PlayedCard> plays)
        {
            if (plays == null || plays.Count == 0) return -1;
            var result = ResolveTrick(plays);
            if (!result.HasWinner) return -1;
            foreach (var p in plays)
                if (p.Seat == result.WinnerSeat) return Strength(p.Card);
            return -1;
        }
    }
}
