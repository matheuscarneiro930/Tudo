using System;
using System.Collections.Generic;

namespace Fodinha.Core
{
    /// <summary>
    /// Estado oficial de um jogador dentro da partida. Mantido apenas pelo host/servidor.
    /// Os clientes recebem uma projeção filtrada (<see cref="PlayerView"/>).
    /// </summary>
    public sealed class PlayerState
    {
        public const int NoBet = -1;

        internal readonly List<Card> hand = new List<Card>();

        /// <summary>Posição na mesa (0..N-1), em sentido horário.</summary>
        public int Seat { get; }

        public string Name { get; set; }

        public int Lives { get; internal set; }

        public bool IsEliminated { get; internal set; }

        /// <summary>Rodada em que foi eliminado (0 = ainda vivo). Usado para a classificação final.</summary>
        public int EliminatedAtRound { get; internal set; }

        /// <summary>Aposta da rodada atual, ou <see cref="NoBet"/> se ainda não apostou.</summary>
        public int Bet { get; internal set; } = NoBet;

        public int TricksWon { get; internal set; }

        /// <summary>Cartas na mão. INFORMAÇÃO PRIVADA: nunca enviar para outros clientes.</summary>
        public IReadOnlyList<Card> Hand => hand;

        public bool IsConnected { get; internal set; } = true;

        /// <summary>Quando verdadeiro, o bot joga por este jogador (ausente, desconectado ou desistiu).</summary>
        public bool IsAutoPlay { get; internal set; }

        /// <summary>Desistiu da partida: será eliminado ao fim da rodada atual.</summary>
        public bool HasForfeited { get; internal set; }

        public int ConsecutiveTimeouts { get; internal set; }

        public bool HasBet => Bet != NoBet;

        internal int LivesAtRoundStart { get; set; }

        /// <summary>Participa da rodada atual (vivo).</summary>
        public bool IsActive => !IsEliminated;

        public PlayerState(int seat, string name)
        {
            if (seat < 0) throw new ArgumentOutOfRangeException(nameof(seat));
            Seat = seat;
            Name = string.IsNullOrEmpty(name) ? "Jogador " + (seat + 1) : name;
        }

        public override string ToString() => Name + " (assento " + Seat + ", vidas " + Lives + ")";
    }
}
