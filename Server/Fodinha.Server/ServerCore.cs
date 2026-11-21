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
        /// <summary>Tempo para um jogador desconectado no lobby voltar antes de perder o lugar (5 min).</summary>
        public float LobbyDisconnectGraceSeconds = 300f;

        /// <summary>Tempo para quem caiu no meio da partida voltar (o bot joga por ele). Depois disso, perde o lugar.</summary>
        public float MatchReconnectSeconds = 300f;

        /// <summary>Partida sem nenhum humano conectado por este tempo é encerrada.</summary>
        public float AbandonedMatchSeconds = 300f;

        /// <summary>Jogador sem sala e sem conexão por este tempo é esquecido (token deixa de valer).</summary>
        public float ForgetPlayerSeconds = 1800f;

        public int MaxRooms = 2000;
        public int MaxPublicRoomsListed = 50;

        /// <summary>Moedas por vídeo assistido, até o saldo chegar em <see cref="RewardCap"/>.</summary>
        public int RewardAmount = 100;
        public int RewardCap = AccountBook.StartingCoins;
        /// <summary>Intervalo mínimo entre duas recompensas da mesma conta.</summary>
        public float RewardCooldownSeconds = 20f;
        public int RankingSize = 50;
        /// <summary>Código VIP do dono (variável VIP_CODE no Render): quem digitar fica sem anúncios. Vazio = desligado.</summary>
        public string VipCode = "";
        /// <summary>Tentativas erradas de código antes de bloquear por <see cref="CodeLockSeconds"/>.</summary>
        public int MaxCodeFailures = 5;
        public float CodeLockSeconds = 600f;
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
        private readonly AccountBook accounts;
        private double now;
        private int idCounter;

        public ServerCore(ServerOptions options = null, IRandomSource rng = null, AccountBook accounts = null)
        {
            this.options = options ?? new ServerOptions();
            this.rng = rng ?? new SystemRandomSource();
            this.accounts = accounts ?? new AccountBook();
        }

        public int RoomCount => rooms.Count;
        public int PlayerCount => byToken.Count;
        public int ConnectedCount => byLink.Count;
        public double Now => now;
        public AccountBook Accounts => accounts;

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
                case Protocol.ToServer.GetProfile: SendProfile(player, 0, null); break;
                case Protocol.ToServer.GetRanking: HandleRanking(player); break;
                case Protocol.ToServer.ClaimReward: HandleClaimReward(player); break;
                case Protocol.ToServer.SetAvatar: HandleSetAvatar(player, Protocol.Payload<AvatarMsg>(env)); break;
                case Protocol.ToServer.BuyItem: HandleBuy(player, Protocol.Payload<ItemMsg>(env)); break;
                case Protocol.ToServer.EquipItem: HandleEquip(player, Protocol.Payload<ItemMsg>(env)); break;
                case Protocol.ToServer.RedeemCode: HandleRedeemCode(player, Protocol.Payload<CodeMsg>(env)); break;
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

                    // Quem caiu e não voltou em 5 minutos perde o lugar (eliminado ao fim da mão).
                    foreach (var m in room.Members.ToList())
                    {
                        if (m.IsBot || m.Left || m.Player.Link != null || m.Player.Room != room) continue;
                        if (now - m.Player.DisconnectedAt > options.MatchReconnectSeconds) RemoveFromRoom(m.Player, room, notify: false);
                        if (!rooms.ContainsKey(room.Code)) break;
                    }
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
                // Conta já salva (ex.: o servidor reiniciou) ou conta nova com 500 moedas.
                var account = accounts.FindByToken(msg.token);
                string token = account != null ? msg.token : Guid.NewGuid().ToString("N");
                player = new Player
                {
                    Id = account?.Id ?? "p" + Guid.NewGuid().ToString("N").Substring(0, 12),
                    Token = token,
                    Name = name
                };
                player.Account = account ?? accounts.Create(player.Id, token, name);
                byToken[player.Token] = player;
            }
            accounts.Rename(player.Account, player.Name);
            accounts.SetAvatar(player.Account, msg.avatar);

            if (byLink.TryGetValue(link, out var previous) && previous != player) previous.Link = null;
            player.Link = link;
            byLink[link] = player;

            link.Send(Protocol.Encode(Protocol.ToClient.Welcome, new WelcomeMsg
            {
                playerId = player.Id, token = player.Token, name = player.Name, coins = player.Account.Coins
            }));
            SendProfile(player, 0, null);

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
                    StartingLives = r.Options.Rules.StartingLives,
                    Stake = r.Options.Stake
                });
            }
            player.Send(Protocol.Encode(Protocol.ToClient.RoomList, list));
        }

        private void HandleCreateRoom(Player player, CreateRoomMsg msg)
        {
            var opts = OfficialOptions(msg?.options);
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
            if (opts.Stake > player.Account.Coins)
            {
                player.SendError(ErrorCodes.NotEnoughCoins, "Você não tem moedas suficientes para uma sala de " + opts.Stake + ".");
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
            if (room.Options.Stake > player.Account.Coins)
            {
                player.SendError(ErrorCodes.NotEnoughCoins, "Essa sala vale " + room.Options.Stake + " moedas e você tem " + player.Account.Coins + ".");
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
            var opts = OfficialOptions(msg?.options);
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

            // Sala valendo moedas: todo humano precisa ter o valor; ele sai do saldo agora e vai para o pote.
            int stake = room.Options.Stake;
            if (stake > 0)
            {
                var broke = room.Members.FirstOrDefault(m => !m.IsBot && m.Player.Account.Coins < stake);
                if (broke != null)
                {
                    player.SendError(ErrorCodes.NotEnoughCoins, broke.Name + " não tem " + stake + " moedas para entrar.");
                    return;
                }
            }
            room.Pot = 0;
            foreach (var m in room.Members.Where(m => !m.IsBot))
            {
                if (stake > 0)
                {
                    int paid = -accounts.AddCoins(m.Player.Account, -stake);
                    room.Pot += paid;
                    SendProfile(m.Player, -paid, "entrada");
                }
                m.Stake = stake;
            }
            // Avatar de cada assento, fixo durante a partida.
            room.SeatAvatars = room.Members.Select(m => m.Avatar).ToList();

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
            // Acerto: o vencedor (se for humano) leva o pote; todos os humanos contam a partida.
            int winnerSeat = room.Engine.WinnerSeat;
            foreach (var m in room.Members.Where(m => !m.IsBot))
            {
                bool won = m.Seat == winnerSeat;
                accounts.RecordGame(m.Player.Account, won);
                if (won && room.Pot > 0)
                {
                    accounts.AddCoins(m.Player.Account, room.Pot);
                    SendProfile(m.Player, room.Pot, "vitoria");
                }
                else SendProfile(m.Player, 0, null);
            }
            room.Pot = 0;

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

            // Partida encerrada antes do fim (ex.: abandonada): quem não saiu recebe a entrada de volta.
            if (room.InMatch && room.Pot > 0)
            {
                foreach (var m in room.Members.Where(m => !m.IsBot && !m.Left && m.Stake > 0))
                {
                    accounts.AddCoins(m.Player.Account, m.Stake);
                    SendProfile(m.Player, m.Stake, "reembolso");
                }
                room.Pot = 0;
            }
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
                    events = events,
                    avatars = room.SeatAvatars
                };
                m.Player.Send(Protocol.Encode(Protocol.ToClient.Match, update));
            }
        }

        // ------------------------------------------------------------------ carteira e ranking

        private void SendProfile(Player player, int delta, string reason)
        {
            var a = player.Account;
            if (a == null) return;
            player.Send(Protocol.Encode(Protocol.ToClient.Profile, new ProfileMsg
            {
                coins = a.Coins,
                games = a.Games,
                wins = a.Wins,
                delta = delta,
                reason = reason,
                canClaimReward = a.Coins < options.RewardCap,
                avatar = a.EffectiveAvatar,
                owned = new List<string>(a.Owned),
                table = a.TableTheme,
                deck = a.DeckTheme,
                noAds = a.NoAds
            }));
        }

        private void HandleRanking(Player player)
        {
            var all = accounts.Ranking();
            var msg = new RankingMsg { myCoins = player.Account.Coins };
            for (int i = 0; i < all.Count; i++)
            {
                if (i < options.RankingSize)
                    msg.top.Add(new RankEntry { position = i + 1, name = all[i].Name, coins = all[i].Coins, wins = all[i].Wins });
                if (all[i] == player.Account) msg.myPosition = i + 1;
            }
            player.Send(Protocol.Encode(Protocol.ToClient.Ranking, msg));
        }

        /// <summary>Recompensa por vídeo: +100 moedas, só enquanto o saldo estiver abaixo de 500, com intervalo mínimo.</summary>
        private void HandleClaimReward(Player player)
        {
            var a = player.Account;
            if (a.Coins >= options.RewardCap)
            {
                player.SendError(ErrorCodes.RewardUnavailable, "Você já tem " + a.Coins + " moedas. O vídeo é para quem tem menos de " + options.RewardCap + ".");
                return;
            }
            if (now - a.LastRewardAt < options.RewardCooldownSeconds)
            {
                player.SendError(ErrorCodes.RewardUnavailable, "Aguarde alguns segundos para o próximo vídeo.");
                return;
            }
            a.LastRewardAt = now;
            int gained = accounts.AddCoins(a, Math.Min(options.RewardAmount, options.RewardCap - a.Coins));
            SendProfile(player, gained, "recompensa");
        }

        // ------------------------------------------------------------------ avatar, loja e código VIP

        private void HandleSetAvatar(Player player, AvatarMsg msg)
        {
            if (msg == null || AvatarSpec.Sanitize(msg.avatar).Length == 0)
            {
                player.SendError(ErrorCodes.BadMessage, "Avatar inválido.");
                return;
            }
            accounts.SetAvatar(player.Account, msg.avatar);
            SendProfile(player, 0, null);
            // Na sala (antes da partida) os outros veem o avatar novo na hora.
            if (player.Room != null && !player.Room.InMatch) BroadcastRoom(player.Room);
        }

        private void HandleBuy(Player player, ItemMsg msg)
        {
            var item = ShopCatalog.Find(msg?.item);
            switch (accounts.Buy(player.Account, msg?.item))
            {
                case PurchaseResult.Ok:
                    SendProfile(player, -item.Price, "compra");
                    break;
                case PurchaseResult.AlreadyOwned:
                    player.SendError(ErrorCodes.Shop, "Você já tem esse item.");
                    break;
                case PurchaseResult.NotEnoughCoins:
                    player.SendError(ErrorCodes.NotEnoughCoins, "Faltam " + (item.Price - player.Account.Coins) + " moedas para comprar " + item.Name + ".");
                    break;
                default:
                    player.SendError(ErrorCodes.Shop, "Item não encontrado.");
                    break;
            }
        }

        private void HandleEquip(Player player, ItemMsg msg)
        {
            if (!accounts.Equip(player.Account, msg?.item))
            {
                player.SendError(ErrorCodes.Shop, "Você ainda não tem esse item.");
                return;
            }
            SendProfile(player, 0, null);
        }

        /// <summary>Código VIP: compara sem vazar tempo e bloqueia depois de várias tentativas erradas.</summary>
        private void HandleRedeemCode(Player player, CodeMsg msg)
        {
            var a = player.Account;
            if (now < a.CodeLockedUntil)
            {
                player.SendError(ErrorCodes.Code, "Muitas tentativas. Tente de novo mais tarde.");
                return;
            }
            if (!VipCodeMatches(msg?.code))
            {
                if (++a.CodeFailures >= options.MaxCodeFailures)
                {
                    a.CodeFailures = 0;
                    a.CodeLockedUntil = now + options.CodeLockSeconds;
                }
                player.SendError(ErrorCodes.Code, "Código inválido.");
                return;
            }
            a.CodeFailures = 0;
            accounts.SetNoAds(a, true);
            SendProfile(player, 0, "vip");
        }

        private bool VipCodeMatches(string code)
        {
            string expected = NormalizeCode(options.VipCode);
            if (expected.Length < 6) return false; // sem código configurado (ou fraco demais): desligado
            var given = System.Text.Encoding.UTF8.GetBytes(NormalizeCode(code));
            var want = System.Text.Encoding.UTF8.GetBytes(expected);
            return given.Length == want.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(given, want);
        }

        private static string NormalizeCode(string code) =>
            string.IsNullOrWhiteSpace(code) || code.Length > 64 ? "" : code.Trim().ToUpperInvariant();

        private static void SendError(IClientLink link, string code, string message)
        {
            link.Send(Protocol.Encode(Protocol.ToClient.Error, new ErrorMsg { code = code, message = message }));
        }

        private string NewId(string prefix) => prefix + (++idCounter).ToString("x") + rng.Next(1 << 20).ToString("x");

        /// <summary>Opções da sala com as regras oficiais forçadas (o app não consegue criar sala com outras regras).</summary>
        private static RoomOptions OfficialOptions(RoomOptions options)
        {
            if (options?.Rules == null) return options;
            var copy = options.Clone();
            copy.Rules = copy.Rules.WithOfficialRules();
            return copy;
        }

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
            public Account Account;   // carteira (persistida)

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
            public int Stake;         // quanto pagou para entrar na partida atual

            public string Name => IsBot ? BotName : Player.Name;

            /// <summary>Bot: avatar sorteado pelo nome (sempre o mesmo). Humano: o da conta.</summary>
            public string Avatar => IsBot ? AvatarSpec.FromSeed(BotName).Encode() : Player.Account?.EffectiveAvatar ?? "";
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
            public int Pot;           // soma das entradas da partida atual
            public List<string> SeatAvatars = new List<string>();

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
                    inMatch = InMatch,
                    stake = Options.Stake,
                    pot = Pot
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
                        owner = m.Id == OwnerId,
                        avatar = m.Avatar
                    });
                }
                return dto;
            }
        }
    }
}
