using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
    /// Todos os eventos são disparados na thread principal da Unity.
    /// </summary>
    public sealed class OnlineClient : MonoBehaviour, IOnlineSender
    {
        public enum ConnectionState { Offline, Connecting, Connected }

        /// <summary>Tempo máximo para conectar (cobre o servidor gratuito acordando).</summary>
        public const float ConnectTimeoutSeconds = 75f;

        /// <summary>Intervalo dos "toques" HTTP que impedem o servidor gratuito de dormir no meio de uma sala.</summary>
        public const float KeepAwakeSeconds = 240f;

        private float nextKeepAwake;

        private static OnlineClient instance;

        private readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<Exception> failures = new ConcurrentQueue<Exception>();
        private readonly Queue<string> pending = new Queue<string>();
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        private ClientWebSocket socket;
        private CancellationTokenSource cts;
        private bool wantConnection;
        private bool welcomed;
        private int attempts;
        private float reconnectAt;
        private float nextPing;
        private int generation;

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
        public string ServerUrl { get; private set; }

        public event Action<ConnectionState> StateChanged;
        public event Action<List<RoomListing>> RoomListReceived;
        public event Action<RoomStateDto> RoomStateReceived;
        public event Action<MatchUpdateDto> MatchReceived;
        public event Action<ErrorMsg> ErrorReceived;
        public event Action<string> LeftRoom;
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

        private async void BeginConnect()
        {
            int myGeneration = ++generation;
            SetState(ConnectionState.Connecting);
            welcomed = false;
            var ws = new ClientWebSocket();
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            var source = new CancellationTokenSource();
            socket = ws;
            cts = source;

            try
            {
                var uri = new Uri(ServerUrl);
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(source.Token))
                {
                    // O servidor gratuito pode estar "dormindo" e levar ~50 s para acordar.
                    timeout.CancelAfter(TimeSpan.FromSeconds(ConnectTimeoutSeconds));
                    await ws.ConnectAsync(uri, timeout.Token);
                }
            }
            catch (Exception e)
            {
                if (myGeneration != generation) return;
                OnConnectionLost("Não foi possível conectar ao servidor.", e);
                return;
            }
            if (myGeneration != generation) return;

            attempts = 0;
            SetState(ConnectionState.Connected);
            _ = Task.Run(() => ReceiveLoop(ws, source.Token));
            SendNow(Protocol.Encode(Protocol.ToServer.Hello, new HelloMsg
            {
                name = GamePrefs.PlayerName,
                token = GamePrefs.OnlineToken,
                version = Protocol.Version
            }));
        }

        private async Task ReceiveLoop(ClientWebSocket ws, CancellationToken token)
        {
            var buffer = new byte[16 * 1024];
            var sb = new StringBuilder();
            try
            {
                while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    var r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
                    if (!r.EndOfMessage) continue;
                    incoming.Enqueue(sb.ToString());
                    sb.Clear();
                }
                failures.Enqueue(new WebSocketException("Conexão encerrada pelo servidor."));
            }
            catch (Exception e)
            {
                if (!token.IsCancellationRequested) failures.Enqueue(e);
            }
        }

        private void OnConnectionLost(string message, Exception e)
        {
            CloseSocket();
            if (e != null) Debug.Log("[Fodinha] Conexão: " + e.Message);
            if (!wantConnection) return;
            attempts++;
            reconnectAt = Time.unscaledTime + Mathf.Min(10f, Mathf.Pow(2f, attempts - 1));
            // Sem sala, não insiste em silêncio: avisa quem pediu a conexão.
            if (Room == null && attempts >= 2) wantConnection = false;
            ConnectFailed?.Invoke(message);
        }

        private void CloseSocket()
        {
            generation++;
            welcomed = false;
            try { cts?.Cancel(); } catch { /* ignorado */ }
            try { socket?.Abort(); } catch { /* ignorado */ }
            try { socket?.Dispose(); } catch { /* ignorado */ }
            socket = null;
            cts = null;
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
            var json = Protocol.Encode(type, payload);
            if (IsConnected) SendNow(json);
            else
            {
                pending.Enqueue(json);
                if (!wantConnection || State == ConnectionState.Offline) Connect();
            }
        }

        private async void SendNow(string json)
        {
            var ws = socket;
            if (ws == null || ws.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(json);
            await sendLock.WaitAsync();
            try
            {
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception e)
            {
                failures.Enqueue(e);
            }
            finally
            {
                sendLock.Release();
            }
        }

        // ------------------------------------------------------------------ recebimento (thread principal)

        private void Update()
        {
            while (incoming.TryDequeue(out var json)) Dispatch(json);

            if (failures.TryDequeue(out var failure))
            {
                while (failures.TryDequeue(out _)) { }
                if (State != ConnectionState.Offline) OnConnectionLost("Conexão perdida. Reconectando...", failure);
            }

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
                    welcomed = true;
                    while (pending.Count > 0) SendNow(pending.Dequeue());
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
