using System;
using System.Collections.Generic;

namespace Fodinha.Core
{
    /// <summary>Fases da partida (máquina de estados).</summary>
    public enum MatchPhase
    {
        /// <summary>Partida criada, aguardando StartMatch.</summary>
        NotStarted = 0,

        /// <summary>Jogadores apostando, um por vez.</summary>
        Betting = 1,

        /// <summary>Jogadores jogando cartas na vaza, um por vez.</summary>
        Playing = 2,

        /// <summary>Vaza completa e resolvida. Aguardando o host chamar ContinueAfterTrick (tempo para animação).</summary>
        TrickComplete = 3,

        /// <summary>Rodada pontuada. Aguardando o host chamar StartNextRound (tempo para mostrar o resultado).</summary>
        RoundComplete = 4,

        /// <summary>Partida encerrada; há um vencedor.</summary>
        MatchOver = 5
    }

    /// <summary>Resultado de uma tentativa de ação. Ações vindas da rede nunca lançam exceção: retornam um código.</summary>
    public enum ActionResult
    {
        Ok = 0,
        WrongPhase,
        NotYourTurn,
        InvalidSeat,
        PlayerEliminated,
        BetOutOfRange,
        BetForbiddenForLastBidder,
        CardNotInHand
    }

    public enum MatchEventType
    {
        MatchStarted,
        RoundStarted,
        CardsDealt,
        ViraRevealed,
        TurnChanged,
        BetPlaced,
        BettingFinished,
        CardPlayed,
        TrickResolved,
        RoundScored,
        LifeLost,
        PlayerEliminated,
        PlayersRevived,
        AutoPlayChanged,
        ConnectionChanged,
        MatchEnded
    }

    /// <summary>
    /// Evento público da partida. Nunca contém informação privada (mãos).
    /// A camada de rede/UI escuta estes eventos para animar e sincronizar.
    /// </summary>
    public sealed class MatchEvent
    {
        public MatchEventType Type { get; }
        public int Seat { get; }
        public int Value { get; }
        public Card? Card { get; }

        public MatchEvent(MatchEventType type, int seat = -1, int value = 0, Card? card = null)
        {
            Type = type;
            Seat = seat;
            Value = value;
            Card = card;
        }

        public override string ToString()
        {
            return Type + (Seat >= 0 ? " assento=" + Seat : "") + (Value != 0 ? " valor=" + Value : "") + (Card.HasValue ? " carta=" + Card.Value : "");
        }
    }

    /// <summary>Resultado de um jogador na rodada.</summary>
    public readonly struct PlayerRoundResult
    {
        public readonly int Seat;
        public readonly int Bet;
        public readonly int TricksWon;
        public readonly int LivesLost;
        public readonly int LivesAfter;
        public readonly bool Eliminated;

        public PlayerRoundResult(int seat, int bet, int tricksWon, int livesLost, int livesAfter, bool eliminated)
        {
            Seat = seat;
            Bet = bet;
            TricksWon = tricksWon;
            LivesLost = livesLost;
            LivesAfter = livesAfter;
            Eliminated = eliminated;
        }

        public bool Hit => Bet == TricksWon;
    }

    /// <summary>Resumo de uma rodada encerrada.</summary>
    public sealed class RoundSummary
    {
        public int RoundNumber { get; }
        public int Cards { get; }
        public IReadOnlyList<PlayerRoundResult> Results { get; }

        /// <summary>Verdadeiro se todos os vivos zeraram ao mesmo tempo e o desempate foi aplicado.</summary>
        public bool SimultaneousEliminationTiebreak { get; }

        public RoundSummary(int roundNumber, int cards, IReadOnlyList<PlayerRoundResult> results, bool tiebreak)
        {
            RoundNumber = roundNumber;
            Cards = cards;
            Results = results;
            SimultaneousEliminationTiebreak = tiebreak;
        }
    }

    /// <summary>O que um jogador específico pode ver de outro jogador.</summary>
    public sealed class PlayerView
    {
        public int Seat { get; internal set; }
        public string Name { get; internal set; }
        public int Lives { get; internal set; }
        public bool IsEliminated { get; internal set; }

        /// <summary>Rodada em que foi eliminado (0 = vivo). Público: usado na classificação final.</summary>
        public int EliminatedAtRound { get; internal set; }

        public int Bet { get; internal set; } = PlayerState.NoBet;
        public int TricksWon { get; internal set; }
        public int HandCount { get; internal set; }
        public bool IsConnected { get; internal set; }
        public bool IsAutoPlay { get; internal set; }

        /// <summary>Cartas deste jogador visíveis para quem está olhando (rodada cega: as cartas na "testa" dos outros).</summary>
        public IReadOnlyList<Card> VisibleCards { get; internal set; } = Array.Empty<Card>();

        public bool HasBet => Bet != PlayerState.NoBet;
    }

    /// <summary>
    /// Projeção da partida do ponto de vista de UM assento. É exatamente o que pode ser enviado para aquele cliente:
    /// contém a própria mão (exceto na rodada cega) e nunca a mão dos outros (exceto as cartas da testa na rodada cega).
    /// Assento -1 = espectador.
    /// </summary>
    public sealed class MatchView
    {
        public const int Spectator = -1;

        public int ViewerSeat { get; internal set; }
        public MatchPhase Phase { get; internal set; }
        public int RoundNumber { get; internal set; }
        public int CardsThisRound { get; internal set; }
        public int TrickNumber { get; internal set; }
        public int DealerSeat { get; internal set; }
        public int CurrentSeat { get; internal set; }
        public bool IsBlindRound { get; internal set; }
        public Card? Vira { get; internal set; }
        public CardComparer Comparer { get; internal set; }
        public RuleSet Rules { get; internal set; }
        public IReadOnlyList<PlayerView> Players { get; internal set; }
        public IReadOnlyList<Card> MyHand { get; internal set; } = Array.Empty<Card>();
        public IReadOnlyList<PlayedCard> CurrentTrick { get; internal set; } = Array.Empty<PlayedCard>();
        public TrickResult LastTrick { get; internal set; }
        public RoundSummary LastRoundSummary { get; internal set; }

        /// <summary>Apostas válidas para quem está olhando, se for a vez dele apostar. Senão, vazio.</summary>
        public IReadOnlyList<int> AllowedBets { get; internal set; } = Array.Empty<int>();

        public int WinnerSeat { get; internal set; } = -1;

        public bool IsMyTurn => ViewerSeat >= 0 && CurrentSeat == ViewerSeat;

        public PlayerView Me => ViewerSeat >= 0 ? Players[ViewerSeat] : null;

        public int ActivePlayerCount
        {
            get
            {
                int count = 0;
                foreach (var p in Players) if (!p.IsEliminated) count++;
                return count;
            }
        }
    }
}
