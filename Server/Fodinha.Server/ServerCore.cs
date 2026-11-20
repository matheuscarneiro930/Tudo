using System;
using System.Collections.Generic;
using System.Linq;
using Fodinha.Core;
using Fodinha.Hosting;
using Fodinha.Net;

namespace Fodinha.Server
{
    /// <summary>Uma conexão de cliente (WebSocket real ou falsa nos testes).</summary>
    public interface IClientLink
    {
        void Send(string json);
        void Close();
    }

    public sealed class ServerOptions
    {
        /// <summary>Tempo para um jogador desconectado no lobby voltar antes de perder o lugar.</summary>
        public float LobbyDisconnectGraceSeconds = 60f;

        /// <summary>Partida sem nenhum humano conectado por este tempo é encerrada.</summary>
        public float AbandonedMatchSeconds = 300f;

        /// <summary>Jogador sem sala e sem conexão por este tempo é esquecido (token deixa de valer).</summary>
        public float ForgetPlayerSeconds = 1800f;

        public int MaxRooms = 2000;
        public int MaxPublicRoomsListed = 50;
        public PacingSettings Pacing = new PacingSettings();
    }

    /// <summary>
    /// Lógica do servidor: jogadores, salas, lobby e partidas. NÃO é thread-safe de propósito:
    /// o host chama todos os métodos a partir de uma única fila (um "ator"), então não há corridas.
    /// As regras do jogo continuam no <see cref="MatchEngine"/>; o ritmo, no <see cref="MatchDirector"/>.
    /// </summary>
    public sealed class ServerCore
    {
        private readonly ServerOptions options;
        private readonly IRandomSource rng;
        private readonly Dictionary<IClientLink, Player> byLink = new Dictionary<IClientLink, Player>();
        private readonly Dictionary<string, Player> byToken = new Dictionary<string, Player>();
        private readonly Dictionary<string, Room> rooms = new Dictionary<string, Room>();
        private double now;
        private int idCounter;

        public ServerCore(ServerOptions options = null, IRandomSource rng = null)
        {
            this.options = options ?? new ServerOptions();
            this.rng = rng ?? new SystemRandomSource();
        }

        public int RoomCount => rooms.Count;
        public int PlayerCount => byToken.Count;
        public int ConnectedCount => byLink.Count;
        public double Now => now;

        internal Room FindRoom(string code) => rooms.TryGetValue(code, out var r) ? r : null;

        // ------------------------------------------------------------------ conexões

        public void OnConnected(IClientLink link)
        {
            // Nada até o "hello".
        }

        public void OnDisconnected(IClientLink link)
        {
            if (!byLink.TryGetValue(link, out var player)) return;
            byLink.Remove(link);
            if (player.Link != link) return; // já reconectou por outra conexão
            player.Link = null;
            player.DisconnectedAt = now;

            var room = player.Room;
            if (room == null) return;
            if (room.InMatch)
            {
                int seat = room.SeatOf(player);
                if (seat >= 0) room.Engine.SetConnected(seat, false);
                PublishMatch(room);
            }
            BroadcastRoom(room);
        }

        public void OnMessage(IClientLink link, string json)
        {
            if (!Protocol.TryDecode(json, out var env))
            {
                SendError(link, ErrorCodes.BadMessage, "Mensagem inválida.");
                return;
            }

            if (env.t == Protocol.ToServer.Hello)
            {
                HandleHello(link, Protocol.Payload<HelloMsg>(env));
                return;
            }
            if (env.t == Protocol.ToServer.Ping)
            {
                link.Send(Protocol.Encode(Protocol.ToClient.Pong));
                return;
            }

            if (!byLink.TryGetValue(link, out var player))
            {
                SendError(link, ErrorCodes.NotHello, "Identifique-se primeiro.");
                return;
            }

            switch (env.t)
            {
                case Protocol.ToServer.ListRooms: HandleListRooms(player); break;
                case Protocol.ToServer.CreateRoom: HandleCreateRoom(player, Protocol.Payload<CreateRoomMsg>(env)); break;
                case Protocol.ToServer.JoinRoom: HandleJoinRoom(player, Protocol.Payload<JoinRoomMsg>(env)); break;
                case Protocol.ToServer.LeaveRoom: HandleLeave(player); break;
                case Protocol.ToServer.UpdateOptions: HandleUpdateOptions(player, Protocol.Payload<UpdateOptionsMsg>(env)); break;
                case Protocol.ToServer.AddBot: HandleAddBot(player); break;
                case Protocol.ToServer.RemoveBot: HandleRemoveBot(player, Protocol.Payload<RemoveBotMsg>(env)); break;
                case Protocol.ToServer.StartMatch: HandleStart(player); break;
                case Protocol.ToServer.Bet: HandleBet(player, Protocol.Payload<BetMsg>(env)); break;
                case Protocol.ToServer.Play: HandlePlay(player, Protocol.Payload<PlayMsg>(env)); break;
                case Protocol.ToServer.Resume: HandleResume(player); break;
                default: SendError(link, ErrorCodes.BadMessage, "Tipo de mensagem desconhecido."); break;
            }
        }

        // ------------------------------------------------------------------ relógio

        public void Tick(float dt)
        {
            if (dt < 0f) dt = 0f;
            now += dt;

            foreach (var room in rooms.Values.ToList())
            {
                if (room.InMatch)
                {
                    room.Director.Tick(dt);
                    if (room.Director.Version != room.PublishedVersion) PublishMatch(room);
                    if (room.Engine.Phase == MatchPhase.MatchOver) EndMatch(room);
                    else if (!room.HasConnectedHuman)
                    {
                        if (room.AbandonedSince < 0) room.AbandonedSince = now;
                        else if (now - room.AbandonedSince > options.AbandonedMatchSeconds) DeleteRoom(room, "abandonada");
                    }
                    else room.AbandonedSince = -1;
                    continue;
                }

                // Lobby: quem caiu e não voltou a tempo perde o lugar.
                foreach (var m in room.Members.ToList())
                {
                    if (m.IsBot || m.Player.Link != null) continue;
                    if (now - m.Player.DisconnectedAt > options.LobbyDisconnectGraceSeconds) RemoveFromRoom(m.Player, room, notify: false);
                }
            }

            // Esquece jogadores sem sala e sem conexão há muito tempo.
            foreach (var p in byToken.Values.ToList())
                if (p.Link == null && p.Room == null && now - p.DisconnectedAt > options.ForgetPlayerSeconds)
                    byToken.Remove(p.Token);
        }

        // ------------------------------------------------------------------ handlers

        private void HandleHello(IClientLink link, HelloMsg msg)
        {
            if (msg == null) { SendError(link, ErrorCodes.BadMessage, "Hello inválido."); return; }
            if (msg.version != Protocol.Version)
            {
                SendError(link, ErrorCodes.Version, "Versão do app diferente da do servidor. Atualize o app.");
                return;
            }

            string name = SanitizeName(msg.name);
            Player player;
            if (!string.IsNullOrEmpty(msg.token) && byToken.TryGetValue(msg.token, out player))
            {
                if (player.Link != null && player.Link != link)
                {
                    var old = player.Link;
                    byLink.Remove(old);
                    old.Close();
                }
                if (player.Room == null || !player.Room.InMatch) player.Name = name;
            }
            else
            {
                player = new Player
                {
                    Id = NewId("p"),
                    Token = Guid.NewGuid().ToString("N"),
                    Name = name
                };
                byToken[player.Token] = player;
            }

            if (byLink.TryGetValue(link, out var previous) && previous != player) previous.Link = null;
            player.Link = link;
            byLink[link] = player;

            link.Send(Protocol.Encode(Protocol.ToClient.Welcome, new WelcomeMsg { playerId = player.Id, token = player.Token, name = player.Name }));

            var room = player.Room;
            if (room == null) return;
            if (room.InMatch)
            {
                int seat = room.SeatOf(player);
                if (seat >= 0) room.Engine.SetConnected(seat, true);
            }
            BroadcastRoom(room);
            if (room.InMatch) PublishMatch(room, force: true);
        }

        private void HandleListRooms(Player player)
        {
            var list = new RoomListMsg();
            foreach (var r in rooms.Values
                         .Where(r => r.Options.IsPublic)
                         .OrderBy(r => r.InMatch)
                         .ThenByDescending(r => r.Members.Count)
                         .Take(options.MaxPublicRoomsListed))
            {
                list.rooms.Add(new RoomListing
                {
                    Code = r.Code,
                    HostName = r.Owner?.Player?.Name ?? "?",
                    Players = r.Members.Count,
                    MaxPlayers = r.Options.MaxPlayers,
                    InProgress = r.InMatch,
                    StartingLives = r.Options.Rules.StartingLives
                });
            }
            player.Send(Protocol.Encode(Protocol.ToClient.RoomList, list));
        }

        private void HandleCreateRoom(Player player, CreateRoomMsg msg)
        {
            var opts = msg?.options;
            if (opts == null || opts.Rules == null || opts.GetValidationErrors().Count > 0)
            {
                player.SendError(ErrorCodes.InvalidOptions, "Configuração da sala inválida.");
                return;
            }
            if (rooms.Count >= options.MaxRooms)
            {
                player.SendError(ErrorCodes.ServerFull, "Servidor lotado. Tente de novo em instantes.");
                return;
            }

            LeaveCurrentRoom(player);

            string code;
            do code = RoomCode.Generate(rng); while (rooms.ContainsKey(code));

            var room = new Room(code, opts.Clone());
            room.Members.Add(new Member { Player = player, Id = player.Id });
            room.OwnerId = player.Id;
            rooms[code] = room;
            player.Room = room;
            BroadcastRoom(room);
        }

        private void HandleJoinRoom(Player player, JoinRoomMsg msg)
        {
            string code = RoomCode.Normalize(msg?.code);
            if (!RoomCode.IsValid(code) || !rooms.TryGetValue(code, out var room))
            {
                player.SendError(ErrorCodes.RoomNotFound, "Sala não encontrada. Confira o código.");
                return;
            }
            if (player.Room == room)
            {
                BroadcastRoom(room);
                if (room.InMatch) PublishMatch(room, force: true);
                return;
            }
            if (room.InMatch)
            {
                player.SendError(ErrorCodes.RoomInMatch, "A partida dessa sala já começou.");
                return;
            }
            if (room.Members.Count >= room.Options.MaxPlayers)
            {
                player.SendError(ErrorCodes.RoomFull, "A sala está cheia.");
                return;
            }

            LeaveCurrentRoom(player);
            room.Members.Add(new Member { Player = player, Id = player.Id });
            player.Room = room;
            BroadcastRoom(room);
        }

        private void HandleLeave(Player player)
        {
            if (player.Room == null)
            {
                player.Send(Protocol.Encode(Protocol.ToClient.LeftRoom, new LeftRoomMsg { reason = "saiu" }));
                return;
            }
            LeaveCurrentRoom(player);
            player.Send(Protocol.Encode(Protocol.ToClient.LeftRoom, new LeftRoomMsg { reason = "saiu" }));
        }

        private void HandleUpdateOptions(Player player, UpdateOptionsMsg msg)
        {
            var room = RequireOwnerInLobby(player);
            if (room == null) return;
            var opts = msg?.options;
            if (opts == null || opts.Rules == null || opts.GetValidationErrors().Count > 0 || opts.MaxPlayers < room.Members.Count)
            {
                player.SendError(ErrorCodes.InvalidOptions, "Configuração inválida (o máximo não pode ser menor que os jogadores na sala).");
                return;
            }
            room.Options = opts.Clone();
            BroadcastRoom(room);
        }

        private void HandleAddBot(Player player)
        {
            var room = RequireOwnerInLobby(player);
            if (room == null) return;
            if (room.Members.Count >= room.Options.MaxPlayers)
            {
                player.SendError(ErrorCodes.RoomFull, "A sala está cheia.");
                return;
            }
            var used = new HashSet<string>(room.Members.Select(m => m.Name));
            string name = BotNames.Pick(1, 0, used)[0];
            room.Members.Add(new Member { IsBot = true, BotName = name, Id = NewId("bot") });
            BroadcastRoom(room);
        }

        private void HandleRemoveBot(Player player, RemoveBotMsg msg)
        {
            var room = RequireOwnerInLobby(player);
            if (room == null) return;
            int index = msg?.index ?? -1;
            if (index < 0 || index >= room.Members.Count || !room.Members[index].IsBot)
            {
                player.SendError(ErrorCodes.BadMessage, "Esse jogador não é um bot.");
                return;
            }
            room.Members.RemoveAt(index);
            BroadcastRoom(room);
        }

        private void HandleStart(Player player)
        {
            var room = RequireOwnerInLobby(player);
            if (room == null) return;
            if (room.Members.Count < RuleSet.MinPlayers)
            {
                player.SendError(ErrorCodes.NeedPlayers, "São necessários pelo menos 2 jogadores (adicione um bot).");
                return;
            }

            var names = room.Members.Select(m => m.Name).ToList();
            var engine = new MatchEngine(room.Options.Rules, names, new SystemRandomSource(rng.Next(int.MaxValue)));
            var director = new MatchDirector(engine, options.Pacing, new SystemRandomSource(rng.Next(int.MaxValue)));
            room.Engine = engine;
            room.Director = director;
            room.PendingEvents.Clear();
            director.EventRaised += room.PendingEvents.Add;

            for (int seat = 0; seat < room.Members.Count; seat++)
            {
                var m = room.Members[seat];
                m.Seat = seat;
                m.Left = false;
                if (m.IsBot) director.SetBotSeat(seat, true);
                else if (m.Player.Link == null) engine.SetConnected(seat, false);
            }

            room.InMatch = true;
            room.AbandonedSince = -1;
            room.PublishedVersion = -1;
            director.Start();
            BroadcastRoom(room);
            PublishMatch(room);
        }

        private void HandleBet(Player player, BetMsg msg)
        {
            var (room, seat) = RequireSeat(player);
            if (room == null) return;
            if (msg == null) { player.SendError(ErrorCodes.BadMessage, "Aposta inválida."); return; }
            Report(player, room.Director.SubmitBet(seat, msg.value));
            PublishMatch(room);
        }

        private void HandlePlay(Player player, PlayMsg msg)
        {
            var (room, seat) = RequireSeat(player);
            if (room == null) return;
            if (msg == null || msg.card < 0 || msg.card >= Card.DeckSize)
            {
                Report(player, ActionResult.CardNotInHand);
                return;
            }
            Report(player, room.Director.SubmitPlay(seat, Card.FromId(msg.card)));
            PublishMatch(room);
        }

        private void HandleResume(Player player)
        {
            var (room, seat) = RequireSeat(player);
            if (room == null) return;
            room.Director.RequestResume(seat);
            PublishMatch(room);
        }

        // ------------------------------------------------------------------ salas

        private Room RequireOwnerInLobby(Player player)
        {
            var room = player.Room;
            if (room == null) { player.SendError(ErrorCodes.NotInRoom, "Você não está numa sala."); return null; }
            if (room.OwnerId != player.Id) { player.SendError(ErrorCodes.NotOwner, "Só o dono da sala pode fazer isso."); return null; }
            if (room.InMatch) { player.SendError(ErrorCodes.RoomInMatch, "A partida já começou."); return null; }
            return room;
        }

        private (Room, int) RequireSeat(Player player)
        {
            var room = player.Room;
            if (room == null || !room.InMatch)
            {
                player.SendError(ErrorCodes.NotInMatch, "Não há partida em andamento.");
                return (null, -1);
            }
            int seat = room.SeatOf(player);
            if (seat < 0)
            {
                player.SendError(ErrorCodes.NotInMatch, "Você não está nesta partida.");
                return (null, -1);
            }
            return (room, seat);
        }

        private static void Report(Player player, ActionResult result)
        {
            if (result == ActionResult.Ok) return;
            player.Send(Protocol.Encode(Protocol.ToClient.Error, new ErrorMsg
            {
                code = ErrorCodes.Action,
                action = (int)result,
                message = result.ToString()
            }));
        }

        private void LeaveCurrentRoom(Player player)
        {
            var room = player.Room;
            if (room == null) return;
            RemoveFromRoom(player, room, notify: true);
        }

        private void RemoveFromRoom(Player player, Room room, bool notify)
        {
            var member = room.Members.FirstOrDefault(m => m.Player == player);
            player.Room = null;
            if (member == null) return;

            if (room.InMatch)
            {
                // Saiu no meio: o bot termina a rodada e ele é eliminado ao fim dela.
                member.Left = true;
                room.Engine.Forfeit(member.Seat);
                room.Engine.SetConnected(member.Seat, false);
            }
            else
            {
                room.Members.Remove(member);
            }

            if (room.OwnerId == player.Id)
            {
                var next = room.Members.FirstOrDefault(m => !m.IsBot && !m.Left);
                room.OwnerId = next?.Id;
            }

            if (!room.Members.Any(m => !m.IsBot && !m.Left))
            {
                DeleteRoom(room, null);
                return;
            }

            // Os que ficaram veem a sala atualizada (quem saiu já não está mais nela).
            BroadcastRoom(room);
            if (room.InMatch) PublishMatch(room, force: notify);
        }

        private void EndMatch(Room room)
        {
            room.InMatch = false;
            room.Director.EventRaised -= room.PendingEvents.Add;
            // Quem saiu ou não voltou perde o lugar; bots e conectados continuam na sala para a revanche.
            room.Members.RemoveAll(m => m.Left || (!m.IsBot && m.Player.Room != room));
            foreach (var m in room.Members.Where(m => !m.IsBot && m.Player.Link == null).ToList())
            {
                room.Members.Remove(m);
                m.Player.Room = null;
            }
            if (room.OwnerId == null || room.Members.All(m => m.Id != room.OwnerId))
                room.OwnerId = room.Members.FirstOrDefault(m => !m.IsBot)?.Id;

            if (!room.Members.Any(m => !m.IsBot))
            {
                DeleteRoom(room, null);
                return;
            }
            BroadcastRoom(room);
        }

        private void DeleteRoom(Room room, string reason)
        {
            rooms.Remove(room.Code);
            foreach (var m in room.Members)
            {
                if (m.IsBot || m.Player.Room != room) continue;
                m.Player.Room = null;
                if (reason != null)
                    m.Player.Send(Protocol.Encode(Protocol.ToClient.LeftRoom, new LeftRoomMsg { reason = reason }));
            }
        }

        // ------------------------------------------------------------------ envio

        private void BroadcastRoom(Room room)
        {
            foreach (var m in room.Members)
            {
                if (m.IsBot || m.Left || m.Player.Link == null || m.Player.Room != room) continue;
                m.Player.Send(Protocol.Encode(Protocol.ToClient.RoomState, room.ToDto(m.Player.Id)));
            }
        }

        /// <summary>Envia a cada humano conectado SÓ a própria visão (<see cref="MatchEngine.BuildView"/>).</summary>
        private void PublishMatch(Room room, bool force = false)
        {
            if (!room.InMatch || room.Engine == null) return;
            if (!force && room.Director.Version == room.PublishedVersion && room.PendingEvents.Count == 0) return;
            room.PublishedVersion = room.Director.Version;

            var events = room.PendingEvents.Select(ViewMapper.ToDto).ToList();
            room.PendingEvents.Clear();

            foreach (var m in room.Members)
            {
                if (m.IsBot || m.Left || m.Player.Link == null || m.Player.Room != room) continue;
                var update = new MatchUpdateDto
                {
                    view = ViewMapper.ToDto(room.Engine.BuildView(m.Seat)),
                    timerSeat = room.Director.TimerSeat,
                    turnRemaining = room.Director.TurnTimeRemaining,
                    turnTotal = room.Director.TurnTimeTotal,
                    phaseWait = room.Director.PhaseWaitRemaining,
                    events = events
                };
                m.Player.Send(Protocol.Encode(Protocol.ToClient.Match, update));
            }
        }

        private static void SendError(IClientLink link, string code, string message)
        {
            link.Send(Protocol.Encode(Protocol.ToClient.Error, new ErrorMsg { code = code, message = message }));
        }

        private string NewId(string prefix) => prefix + (++idCounter).ToString("x") + rng.Next(1 << 20).ToString("x");

        public static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Jogador";
            name = new string(name.Where(c => !char.IsControl(c) && c != '<' && c != '>').ToArray()).Trim();
            if (name.Length > 14) name = name.Substring(0, 14);
            return name.Length == 0 ? "Jogador" : name;
        }

        // ------------------------------------------------------------------ tipos internos

        internal sealed class Player
        {
            public string Id;
            public string Token;
            public string Name;
            public IClientLink Link;
            public Room Room;
            public double DisconnectedAt;

            public void Send(string json) => Link?.Send(json);

            public void SendError(string code, string message) =>
                Send(Protocol.Encode(Protocol.ToClient.Error, new ErrorMsg { code = code, message = message }));
        }

        internal sealed class Member
        {
            public string Id;
            public Player Player;     // null para bot
            public bool IsBot;
            public string BotName;
            public int Seat = -1;     // assento na partida atual
            public bool Left;         // saiu no meio da partida

            public string Name => IsBot ? BotName : Player.Name;
        }

        internal sealed class Room
        {
            public readonly string Code;
            public RoomOptions Options;
            public string OwnerId;
            public readonly List<Member> Members = new List<Member>();
            public bool InMatch;
            public MatchEngine Engine;
            public MatchDirector Director;
            public int PublishedVersion = -1;
            public readonly List<MatchEvent> PendingEvents = new List<MatchEvent>();
            public double AbandonedSince = -1;

            public Room(string code, RoomOptions options)
            {
                Code = code;
                Options = options;
            }

            public Member Owner => Members.FirstOrDefault(m => m.Id == OwnerId);

            public bool HasConnectedHuman => Members.Any(m => !m.IsBot && !m.Left && m.Player.Link != null && m.Player.Room == this);

            public int SeatOf(Player p)
            {
                var m = Members.FirstOrDefault(x => x.Player == p);
                return m != null && InMatch ? m.Seat : -1;
            }

            public RoomStateDto ToDto(string you)
            {
                var dto = new RoomStateDto
                {
                    code = Code,
                    isPublic = Options.IsPublic,
                    maxPlayers = Options.MaxPlayers,
                    rules = Options.Rules,
                    ownerId = OwnerId,
                    you = you,
                    inMatch = InMatch
                };
                foreach (var m in Members)
                {
                    if (m.Left) continue;
                    dto.members.Add(new MemberDto
                    {
                        id = m.Id,
                        name = m.Name,
                        bot = m.IsBot,
                        connected = m.IsBot || m.Player.Link != null,
                        owner = m.Id == OwnerId
                    });
                }
                return dto;
            }
        }
    }
}
