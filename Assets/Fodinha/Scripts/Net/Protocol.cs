using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Fodinha.Net
{
    /// <summary>
    /// Protocolo entre o app e o servidor: mensagens JSON em WebSocket, no formato {"t": tipo, "d": dados}.
    /// O cliente só envia PEDIDOS; o servidor valida tudo e responde com estado (sala) e visão filtrada (partida).
    /// </summary>
    public static class Protocol
    {
        /// <summary>Muda quando o formato das mensagens muda de forma incompatível.</summary>
        public const int Version = 1;

        /// <summary>Caminho do WebSocket no servidor.</summary>
        public const string Path = "/ws";

        /// <summary>Tamanho máximo de uma mensagem do cliente (proteção do servidor).</summary>
        public const int MaxClientMessageBytes = 16 * 1024;

        /// <summary>Pedidos do cliente para o servidor.</summary>
        public static class ToServer
        {
            public const string Hello = "hello";
            public const string ListRooms = "list_rooms";
            public const string CreateRoom = "create_room";
            public const string JoinRoom = "join_room";
            public const string LeaveRoom = "leave_room";
            public const string UpdateOptions = "update_options";
            public const string AddBot = "add_bot";
            public const string RemoveBot = "remove_bot";
            public const string StartMatch = "start_match";
            public const string Bet = "bet";
            public const string Play = "play";
            public const string Resume = "resume";
            public const string Ping = "ping";
            public const string GetProfile = "get_profile";
            public const string GetRanking = "get_ranking";
            public const string ClaimReward = "claim_reward";
        }

        /// <summary>Mensagens do servidor para o cliente.</summary>
        public static class ToClient
        {
            public const string Welcome = "welcome";
            public const string RoomList = "room_list";
            public const string RoomState = "room_state";
            public const string Match = "match";
            public const string LeftRoom = "left_room";
            public const string Error = "error";
            public const string Pong = "pong";
            public const string Profile = "profile";
            public const string Ranking = "ranking";
        }

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            Formatting = Formatting.None
        };

        private static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);

        public static string Encode(string type, object payload = null)
        {
            var env = new Envelope { t = type, d = payload != null ? JToken.FromObject(payload, Serializer) : null };
            return JsonConvert.SerializeObject(env, Settings);
        }

        public static bool TryDecode(string json, out Envelope envelope)
        {
            envelope = null;
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                envelope = JsonConvert.DeserializeObject<Envelope>(json, Settings);
                return envelope != null && !string.IsNullOrEmpty(envelope.t);
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>Lê os dados da mensagem. Retorna null se não houver ou se forem inválidos.</summary>
        public static T Payload<T>(Envelope envelope) where T : class
        {
            if (envelope?.d == null || envelope.d.Type == JTokenType.Null) return null;
            try
            {
                return envelope.d.ToObject<T>(Serializer);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>Envelope de toda mensagem.</summary>
    public sealed class Envelope
    {
        public string t;
        public JToken d;
    }

    /// <summary>Códigos de erro enviados pelo servidor.</summary>
    public static class ErrorCodes
    {
        public const string BadMessage = "bad_message";
        public const string Version = "version";
        public const string NotHello = "not_hello";
        public const string RoomNotFound = "room_not_found";
        public const string RoomFull = "room_full";
        public const string RoomInMatch = "room_in_match";
        public const string NotInRoom = "not_in_room";
        public const string NotOwner = "not_owner";
        public const string InvalidOptions = "invalid_options";
        public const string NeedPlayers = "need_players";
        public const string NotInMatch = "not_in_match";
        public const string Action = "action";
        public const string ServerFull = "server_full";
        public const string NotEnoughCoins = "not_enough_coins";
        public const string RewardUnavailable = "reward_unavailable";
    }
}
