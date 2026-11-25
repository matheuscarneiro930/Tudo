using System;
using System.Collections.Generic;
using Fodinha.Game;
using Fodinha.Hosting;
using Fodinha.Net;
using UnityEngine;

namespace Fodinha.Online
{
    /// <summary>Quem envia pedidos ao servidor (o <see cref="OnlineClient"/>, ou um falso nos testes).</summary>
    public interface IOnlineSender
    {
        void Send(string type, object payload = null);
    }

    /// <summary>
    /// Conexão com o servidor (WebSocket). Vive entre as cenas. Reconecta sozinho enquanto o jogador estiver online
    /// e se reapresenta com o token salvo, recuperando o lugar na sala/partida.
    /// A conexão (<see cref="ISocketTransport"/>) é consultada a cada quadro, então tudo acontece na thread principal:
    /// funciona igual no celular, no PC e no navegador.
    /// </summary>
    public sealed class OnlineClient : MonoBehaviour, IOnlineSender
    {
        public enum ConnectionState { Offline, Connecting, Connected }

        /// <summary>Tempo máximo para conectar (cobre o servidor gratuito acordando).</summary>
        public const float ConnectTimeoutSeconds = 75f;

        /// <summary>Intervalo dos "toques" HTTP que impedem o servidor gratuito de dormir no meio de uma sala.</summary>
        public const float KeepAwakeSeconds = 240f;

        private static OnlineClient instance;

        private readonly Queue<string> pending = new Queue<string>();
        private ISocketTransport transport;
        private float connectStartedAt;
        private bool wantConnection;
        private bool welcomed;
        private int attempts;
        private float reconnectAt;
        private float nextPing;
        private float nextKeepAwake;

        public static OnlineClient Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("OnlineClient");
                    instance = go.AddComponent<OnlineClient>();
                    if (Application.isPlaying) DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        public static bool Exists => instance != null;

        public ConnectionState State { get; private set; } = ConnectionState.Offline;
        public bool IsConnected => State == ConnectionState.Connected && welcomed;
        public string PlayerId { get; private set; }
        public RoomStateDto Room { get; private set; }
        public MatchUpdateDto LastMatch { get; private set; }
        /// <summary>Última carteira recebida (moedas fictícias). Null antes de conectar.</summary>
        public ProfileMsg Profile { get; private set; }
        public string ServerUrl { get; private set; }

        public event Action<ConnectionState> StateChanged;
        public event Action<List<RoomListing>> RoomListReceived;
        public event Action<RoomStateDto> RoomStateReceived;
        public event Action<MatchUpdateDto> MatchReceived;
        public event Action<ErrorMsg> ErrorReceived;
        public event Action<string> LeftRoom;
        public event Action<ProfileMsg> ProfileReceived;
        public event Action<RankingMsg> RankingReceived;
        /// <summary>Não foi possível conectar (mensagem para o jogador).</summary>
        public event Action<string> ConnectFailed;

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            CloseSocket();
        }

        // ------------------------------------------------------------------ conexão

        /// <summary>Conecta (se ainda não estiver) ao servidor configurado.</summary>
        public void Connect()
        {
            string url = GamePrefs.ServerUrl;
            if (ServerUrl != url && State != ConnectionState.Offline) CloseSocket();
            ServerUrl = url;
            wantConnection = true;
            if (State == ConnectionState.Offline) BeginConnect();
        }

        /// <summary>Desconecta e para de tentar reconectar.</summary>
        public void Disconnect()
        {
            wantConnection = false;
            pending.Clear();
            Room = null;
            LastMatch = null;
            CloseSocket();
        }

        private void BeginConnect()
        {
            welcomed = false;
            SetState(ConnectionState.Connecting);
            connectStartedAt = Time.unscaledTime;
            try
            {
                transport = SocketTransport.Open(ServerUrl);
            }
            catch (Exception e)
            {
                OnConnectionLost("Endereço do servidor inválido.", e.Message);
            }
        }

        private void OnConnectionLost(string message, string detail)
        {
            CloseSocket();
            if (!string.IsNullOrEmpty(detail)) Debug.Log("[Fodinha] Conexão: " + detail);
            if (!wantConnection) return;
            attempts++;
            reconnectAt = Time.unscaledTime + Mathf.Min(10f, Mathf.Pow(2f, attempts - 1));
            // Sem sala, não insiste em silêncio: avisa quem pediu a conexão.
            if (Room == null && attempts >= 2) wantConnection = false;
            ConnectFailed?.Invoke(message);
        }

        private void CloseSocket()
        {
            welcomed = false;
            try { transport?.Close(); } catch { /* ignorado */ }
            transport = null;
            SetState(ConnectionState.Offline);
        }

        private void SetState(ConnectionState s)
        {
            if (State == s) return;
            State = s;
            StateChanged?.Invoke(s);
        }

        // ------------------------------------------------------------------ envio

        /// <summary>Envia um pedido. Se ainda não estiver conectado, guarda e envia logo após a apresentação.</summary>
        public void Send(string type, object payload = null)
        {
            // Endereço do servidor mudou (Configurações): fecha a conexão antiga e conecta no novo.
            if (State != ConnectionState.Offline && ServerUrl != GamePrefs.ServerUrl) CloseSocket();

            var json = Protocol.Encode(type, payload);
            if (IsConnected) SendNow(json);
            else
            {
                pending.Enqueue(json);
                if (!wantConnection || State == ConnectionState.Offline) Connect();
            }
        }

        private void SendNow(string json)
        {
            if (transport != null && transport.Status == SocketStatus.Open) transport.Send(json);
        }

        // ------------------------------------------------------------------ a cada quadro (thread principal)

        private void Update()
        {
            PollTransport();

            if (wantConnection && State == ConnectionState.Offline && Time.unscaledTime >= reconnectAt) BeginConnect();

            if (IsConnected && Time.unscaledTime >= nextPing)
            {
                nextPing = Time.unscaledTime + 15f;
                SendNow(Protocol.Encode(Protocol.ToServer.Ping));
            }

            if (Room != null && Time.unscaledTime >= nextKeepAwake)
            {
                nextKeepAwake = Time.unscaledTime + KeepAwakeSeconds;
                WakeUpServer();
            }
        }

        private void PollTransport()
        {
            if (transport == null) return;
            var t = transport;

            if (State == ConnectionState.Connecting)
            {
                if (t.Status == SocketStatus.Open)
                {
                    attempts = 0;
                    SetState(ConnectionState.Connected);
                    SendNow(Protocol.Encode(Protocol.ToServer.Hello, new HelloMsg
                    {
                        name = GamePrefs.PlayerName,
                        token = GamePrefs.OnlineToken,
                        version = Protocol.Version,
                        avatar = GamePrefs.Avatar
                    }));
                }
                else if (t.Status == SocketStatus.Closed)
                {
                    OnConnectionLost("Não foi possível conectar ao servidor.", t.Error);
                    return;
                }
                else if (Time.unscaledTime - connectStartedAt > ConnectTimeoutSeconds)
                {
                    OnConnectionLost("Não foi possível conectar ao servidor.", "tempo esgotado");
                    return;
                }
            }

            // Mensagens primeiro (inclusive as que chegaram logo antes de a conexão cair).
            while (transport == t && t.TryReceive(out var json)) Dispatch(json);

            if (transport == t && State == ConnectionState.Connected && t.Status == SocketStatus.Closed)
                OnConnectionLost("Conexão perdida. Reconectando...", t.Error);
        }

        /// <summary>
        /// Faz uma requisição HTTP ao /health do servidor (sem esperar resposta). Acorda o servidor gratuito
        /// antes do jogador tocar em "Criar sala" e o mantém acordado durante as partidas.
        /// </summary>
        public void WakeUpServer()
        {
            string url = HealthUrl(GamePrefs.ServerUrl);
            if (url == null || !Application.isPlaying) return;
            var request = UnityEngine.Networking.UnityWebRequest.Get(url);
            request.timeout = 90;
            request.SendWebRequest().completed += _ => request.Dispose();
        }

        /// <summary>wss://host/ws → https://host/health (ws:// → http://).</summary>
        public static string HealthUrl(string serverUrl)
        {
            if (string.IsNullOrEmpty(serverUrl)) return null;
            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri)) return null;
            string scheme = uri.Scheme == "wss" ? "https" : uri.Scheme == "ws" ? "http" : null;
            if (scheme == null) return null;
            return new UriBuilder(scheme, uri.Host, uri.Port, "/health").Uri.ToString();
        }

        /// <summary>Processa uma mensagem do servidor (público para testes).</summary>
        public void Dispatch(string json)
        {
            if (!Protocol.TryDecode(json, out var env)) return;
            switch (env.t)
            {
                case Protocol.ToClient.Welcome:
                    var w = Protocol.Payload<WelcomeMsg>(env);
                    if (w == null) return;
                    PlayerId = w.playerId;
                    GamePrefs.OnlineToken = w.token;
                    Game.Ads.AdService.RewardUserId = w.playerId ?? "";
                    welcomed = true;
                    while (pending.Count > 0) SendNow(pending.Dequeue());
                    break;

                case Protocol.ToClient.Profile:
                    var prof = Protocol.Payload<ProfileMsg>(env);
                    if (prof == null) return;
                    Profile = prof;
                    ProfileReceived?.Invoke(prof);
                    break;

                case Protocol.ToClient.Ranking:
                    var rank = Protocol.Payload<RankingMsg>(env);
                    if (rank != null) RankingReceived?.Invoke(rank);
                    break;

                case Protocol.ToClient.RoomList:
                    RoomListReceived?.Invoke(Protocol.Payload<RoomListMsg>(env)?.rooms ?? new List<RoomListing>());
                    break;

                case Protocol.ToClient.RoomState:
                    Room = Protocol.Payload<RoomStateDto>(env);
                    if (Room != null && !Room.inMatch && LastMatch != null && LastMatch.view.phase != (int)Core.MatchPhase.MatchOver)
                        LastMatch = null;
                    if (Room != null) RoomStateReceived?.Invoke(Room);
                    break;

                case Protocol.ToClient.Match:
                    var m = Protocol.Payload<MatchUpdateDto>(env);
                    if (m?.view == null) return;
                    LastMatch = m;
                    MatchReceived?.Invoke(m);
                    break;

                case Protocol.ToClient.LeftRoom:
                    Room = null;
                    LastMatch = null;
                    LeftRoom?.Invoke(Protocol.Payload<LeftRoomMsg>(env)?.reason ?? "");
                    break;

                case Protocol.ToClient.Error:
                    var err = Protocol.Payload<ErrorMsg>(env);
                    if (err == null) return;
                    if (err.code == ErrorCodes.Version) wantConnection = false;
                    ErrorReceived?.Invoke(err);
                    break;
            }
        }

        /// <summary>Sai da sala atual (e da partida, se houver).</summary>
        public void LeaveRoom()
        {
            if (Room == null) return;
            Send(Protocol.ToServer.LeaveRoom);
            Room = null;
            LastMatch = null;
        }

        /// <summary>Texto amigável para um erro do servidor.</summary>
        public static string Describe(ErrorMsg e)
        {
            if (e == null) return "";
            if (e.code == ErrorCodes.Action && e.action >= 0) return Game.UI.MatchScreen.Describe((Core.ActionResult)e.action);
            return string.IsNullOrEmpty(e.message) ? e.code : e.message;
        }
    }
}
