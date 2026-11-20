using System.Collections.Generic;
using Fodinha.Core;
using Fodinha.Hosting;

namespace Fodinha.Net
{
    // Dados das mensagens. Campos públicos e nomes curtos: é o que trafega na rede.

    public sealed class HelloMsg
    {
        public string name;
        /// <summary>Token secreto do jogador (para reconectar e recuperar o lugar na sala). Vazio na 1ª vez.</summary>
        public string token;
        public int version;
    }

    public sealed class WelcomeMsg
    {
        public string playerId;
        public string token;
        public string name;
    }

    public sealed class CreateRoomMsg
    {
        public RoomOptions options;
    }

    public sealed class JoinRoomMsg
    {
        public string code;
    }

    public sealed class UpdateOptionsMsg
    {
        public RoomOptions options;
    }

    public sealed class RemoveBotMsg
    {
        /// <summary>Posição do bot na lista de membros.</summary>
        public int index;
    }

    public sealed class BetMsg
    {
        public int value;
    }

    public sealed class PlayMsg
    {
        /// <summary>Id da carta (0..39).</summary>
        public int card;
    }

    public sealed class RoomListMsg
    {
        public List<RoomListing> rooms = new List<RoomListing>();
    }

    public sealed class LeftRoomMsg
    {
        public string reason;
    }

    public sealed class ErrorMsg
    {
        public string code;
        public string message;
        /// <summary>Quando code = "action": o <see cref="ActionResult"/> recusado.</summary>
        public int action = -1;
    }

    /// <summary>Membro da sala (lobby).</summary>
    public sealed class MemberDto
    {
        public string id;
        public string name;
        public bool bot;
        public bool connected;
        public bool owner;
    }

    /// <summary>Estado da sala visto por um jogador.</summary>
    public sealed class RoomStateDto
    {
        public string code;
        public bool isPublic;
        public int maxPlayers;
        public RuleSet rules;
        public string ownerId;
        /// <summary>Id de quem recebe esta mensagem.</summary>
        public string you;
        public bool inMatch;
        public List<MemberDto> members = new List<MemberDto>();

        public bool IAmOwner => you != null && you == ownerId;
    }

    /// <summary>Atualização da partida para UM jogador: a visão dele, os relógios e os eventos públicos.</summary>
    public sealed class MatchUpdateDto
    {
        public ViewDto view;
        public int timerSeat = -1;
        public float turnRemaining;
        public float turnTotal;
        public float phaseWait;
        public List<EventDto> events = new List<EventDto>();
    }

    public sealed class EventDto
    {
        public int type;
        public int seat = -1;
        public int value;
        public int card = -1;
    }

    public sealed class ViewDto
    {
        public int viewer;
        public int phase;
        public int round;
        public int cards;
        public int trick;
        public int dealer;
        public int current;
        public bool blind;
        public int vira = -1;
        public RuleSet rules;
        public List<PlayerDto> players = new List<PlayerDto>();
        public List<int> hand = new List<int>();
        public List<PlayDto> table = new List<PlayDto>();
        public TrickDto lastTrick;
        public RoundDto lastRound;
        public List<int> allowed = new List<int>();
        public int winner = -1;
    }

    public sealed class PlayerDto
    {
        public int seat;
        public string name;
        public int lives;
        public bool elim;
        public int elimRound;
        public int bet = -1;
        public int won;
        public int handCount;
        public bool connected;
        public bool auto;
        public List<int> visible = new List<int>();
    }

    public sealed class PlayDto
    {
        public int seat;
        public int card;
    }

    public sealed class TrickDto
    {
        public int winner = -1;
        public List<PlayDto> plays = new List<PlayDto>();
        public List<int> canceled = new List<int>();
    }

    public sealed class RoundDto
    {
        public int round;
        public int cards;
        public bool tiebreak;
        public List<ResultDto> results = new List<ResultDto>();
    }

    public sealed class ResultDto
    {
        public int seat;
        public int bet;
        public int won;
        public int lost;
        public int livesAfter;
        public bool elim;
    }
}
