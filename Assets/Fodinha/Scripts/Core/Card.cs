using System;

namespace Fodinha.Core
{
    /// <summary>
    /// Valores das cartas, em ordem CRESCENTE de força normal (hierarquia do Truco):
    /// 4 &lt; 5 &lt; 6 &lt; 7 &lt; Q &lt; J &lt; K &lt; A &lt; 2 &lt; 3.
    /// A ordem também define a sequência da vira (o próximo valor vira manilha; depois do 3 vem o 4).
    /// </summary>
    public enum Rank : byte
    {
        Four = 0,
        Five = 1,
        Six = 2,
        Seven = 3,
        Queen = 4,
        Jack = 5,
        King = 6,
        Ace = 7,
        Two = 8,
        Three = 9
    }

    /// <summary>
    /// Naipes. O valor numérico é a força do naipe entre manilhas (maior = mais forte):
    /// Paus (zap) &gt; Copas &gt; Espadas &gt; Ouros.
    /// </summary>
    public enum Suit : byte
    {
        Diamonds = 0, // Ouros
        Spades = 1,   // Espadas
        Hearts = 2,   // Copas
        Clubs = 3     // Paus
    }

    public static class RankExtensions
    {
        public const int RankCount = 10;

        /// <summary>Próximo valor na sequência circular (usado para descobrir a manilha a partir da vira).</summary>
        public static Rank Next(this Rank rank)
        {
            return (Rank)(((int)rank + 1) % RankCount);
        }

        public static string Symbol(this Rank rank)
        {
            switch (rank)
            {
                case Rank.Four: return "4";
                case Rank.Five: return "5";
                case Rank.Six: return "6";
                case Rank.Seven: return "7";
                case Rank.Queen: return "Q";
                case Rank.Jack: return "J";
                case Rank.King: return "K";
                case Rank.Ace: return "A";
                case Rank.Two: return "2";
                case Rank.Three: return "3";
                default: throw new ArgumentOutOfRangeException(nameof(rank));
            }
        }
    }

    public static class SuitExtensions
    {
        public const int SuitCount = 4;

        public static string Symbol(this Suit suit)
        {
            switch (suit)
            {
                case Suit.Diamonds: return "♦";
                case Suit.Spades: return "♠";
                case Suit.Hearts: return "♥";
                case Suit.Clubs: return "♣";
                default: throw new ArgumentOutOfRangeException(nameof(suit));
            }
        }

        public static string PortugueseName(this Suit suit)
        {
            switch (suit)
            {
                case Suit.Diamonds: return "Ouros";
                case Suit.Spades: return "Espadas";
                case Suit.Hearts: return "Copas";
                case Suit.Clubs: return "Paus";
                default: throw new ArgumentOutOfRangeException(nameof(suit));
            }
        }
    }

    /// <summary>
    /// Uma carta do baralho de 40 cartas. Imutável.
    /// O <see cref="Id"/> (0 a 39) cabe em 1 byte e é o formato usado para trafegar na rede.
    /// </summary>
    [Serializable]
    public readonly struct Card : IEquatable<Card>
    {
        public const int DeckSize = 40;

        public readonly Rank Rank;
        public readonly Suit Suit;

        public Card(Rank rank, Suit suit)
        {
            if ((int)rank < 0 || (int)rank >= RankExtensions.RankCount) throw new ArgumentOutOfRangeException(nameof(rank));
            if ((int)suit < 0 || (int)suit >= SuitExtensions.SuitCount) throw new ArgumentOutOfRangeException(nameof(suit));
            Rank = rank;
            Suit = suit;
        }

        /// <summary>Identificador único 0..39 (naipe * 10 + valor).</summary>
        public byte Id => (byte)((int)Suit * RankExtensions.RankCount + (int)Rank);

        public static Card FromId(int id)
        {
            if (id < 0 || id >= DeckSize) throw new ArgumentOutOfRangeException(nameof(id), "Id de carta deve estar entre 0 e 39.");
            return new Card((Rank)(id % RankExtensions.RankCount), (Suit)(id / RankExtensions.RankCount));
        }

        /// <summary>
        /// Converte texto em carta. Aceita o valor (4 5 6 7 Q J K A 2 3) seguido do naipe,
        /// em símbolo (♣ ♥ ♠ ♦) ou letra em português (P = Paus, C = Copas, E = Espadas, O = Ouros).
        /// Exemplos: "4P", "7♥", "AE", "3O".
        /// </summary>
        public static Card Parse(string text)
        {
            if (!TryParse(text, out var card)) throw new FormatException("Carta inválida: '" + text + "'.");
            return card;
        }

        public static bool TryParse(string text, out Card card)
        {
            card = default;
            if (string.IsNullOrEmpty(text) || text.Length != 2) return false;

            Rank rank;
            switch (char.ToUpperInvariant(text[0]))
            {
                case '4': rank = Rank.Four; break;
                case '5': rank = Rank.Five; break;
                case '6': rank = Rank.Six; break;
                case '7': rank = Rank.Seven; break;
                case 'Q': rank = Rank.Queen; break;
                case 'J': rank = Rank.Jack; break;
                case 'K': rank = Rank.King; break;
                case 'A': rank = Rank.Ace; break;
                case '2': rank = Rank.Two; break;
                case '3': rank = Rank.Three; break;
                default: return false;
            }

            Suit suit;
            switch (char.ToUpperInvariant(text[1]))
            {
                case 'P': case '♣': suit = Suit.Clubs; break;
                case 'C': case '♥': suit = Suit.Hearts; break;
                case 'E': case '♠': suit = Suit.Spades; break;
                case 'O': case '♦': suit = Suit.Diamonds; break;
                default: return false;
            }

            card = new Card(rank, suit);
            return true;
        }

        public bool Equals(Card other) => Rank == other.Rank && Suit == other.Suit;
        public override bool Equals(object obj) => obj is Card other && Equals(other);
        public override int GetHashCode() => Id;
        public static bool operator ==(Card a, Card b) => a.Equals(b);
        public static bool operator !=(Card a, Card b) => !a.Equals(b);

        public override string ToString() => Rank.Symbol() + Suit.Symbol();
    }

    /// <summary>Carta jogada na mesa por um assento.</summary>
    [Serializable]
    public readonly struct PlayedCard
    {
        public readonly int Seat;
        public readonly Card Card;

        public PlayedCard(int seat, Card card)
        {
            Seat = seat;
            Card = card;
        }

        public override string ToString() => "Assento " + Seat + ": " + Card;
    }
}
