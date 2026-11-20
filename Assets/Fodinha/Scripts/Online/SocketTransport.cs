using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Fodinha.Online
{
    public enum SocketStatus { Connecting, Open, Closed }

    /// <summary>
    /// Uma conexão WebSocket de texto, consultada a cada quadro (sem callbacks em outras threads).
    /// Celular/PC usam o ClientWebSocket do .NET; no navegador (WebGL) usa o WebSocket do JavaScript.
    /// </summary>
    public interface ISocketTransport
    {
        SocketStatus Status { get; }
        string Error { get; }
        void Send(string text);
        bool TryReceive(out string text);
        void Close();
    }

    public static class SocketTransport
    {
        public static ISocketTransport Open(string url)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new BrowserSocketTransport(url);
#else
            return new NativeSocketTransport(url);
#endif
        }
    }

    /// <summary>WebSocket do .NET: conecta e recebe numa tarefa em segundo plano; a fila é lida na thread principal.</summary>
    public sealed class NativeSocketTransport : ISocketTransport
    {
        private readonly ClientWebSocket ws = new ClientWebSocket();
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
        private readonly object sendGate = new object();
        private Task sendChain = Task.CompletedTask;
        private volatile SocketStatus status = SocketStatus.Connecting;
        private volatile string error = "";

        public NativeSocketTransport(string url)
        {
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            Task.Run(() => Run(url));
        }

        public SocketStatus Status => status;
        public string Error => error;

        private async Task Run(string url)
        {
            try
            {
                await ws.ConnectAsync(new Uri(url), cts.Token);
                status = SocketStatus.Open;

                var buffer = new byte[16 * 1024];
                var sb = new StringBuilder();
                while (ws.State == WebSocketState.Open && !cts.IsCancellationRequested)
                {
                    var r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
                    if (!r.EndOfMessage) continue;
                    incoming.Enqueue(sb.ToString());
                    sb.Clear();
                }
            }
            catch (Exception e)
            {
                if (!cts.IsCancellationRequested) error = e.Message;
            }
            status = SocketStatus.Closed;
        }

        public void Send(string text)
        {
            if (status != SocketStatus.Open) return;
            var bytes = new ArraySegment<byte>(Encoding.UTF8.GetBytes(text));
            lock (sendGate)
            {
                // Um envio de cada vez, na ordem (o ClientWebSocket não aceita envios simultâneos).
                sendChain = sendChain.ContinueWith(async _ =>
                {
                    try { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token); }
                    catch (Exception e) { error = e.Message; status = SocketStatus.Closed; }
                }, TaskScheduler.Default).Unwrap();
            }
        }

        public bool TryReceive(out string text) => incoming.TryDequeue(out text);

        public void Close()
        {
            status = SocketStatus.Closed;
            try { cts.Cancel(); } catch { /* ignorado */ }
            try { ws.Abort(); } catch { /* ignorado */ }
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    /// <summary>WebSocket do navegador (Assets/Plugins/WebGL/FodinhaWebSocket.jslib).</summary>
    public sealed class BrowserSocketTransport : ISocketTransport
    {
        [DllImport("__Internal")] private static extern int FodinhaWS_Connect(string url);
        [DllImport("__Internal")] private static extern int FodinhaWS_State(int id);
        [DllImport("__Internal")] private static extern void FodinhaWS_Send(int id, string text);
        [DllImport("__Internal")] private static extern string FodinhaWS_Poll(int id);
        [DllImport("__Internal")] private static extern void FodinhaWS_Close(int id);

        private readonly int id;
        private bool closed;

        public BrowserSocketTransport(string url) => id = FodinhaWS_Connect(url);

        public SocketStatus Status
        {
            get
            {
                if (closed) return SocketStatus.Closed;
                switch (FodinhaWS_State(id))
                {
                    case 0: return SocketStatus.Connecting;
                    case 1: return SocketStatus.Open;
                    default: return SocketStatus.Closed;
                }
            }
        }

        public string Error => "";

        public void Send(string text)
        {
            if (!closed) FodinhaWS_Send(id, text);
        }

        public bool TryReceive(out string text)
        {
            text = closed ? null : FodinhaWS_Poll(id);
            return text != null;
        }

        public void Close()
        {
            if (closed) return;
            closed = true;
            FodinhaWS_Close(id);
        }
    }
#endif
}
