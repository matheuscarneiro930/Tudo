using Fodinha.Game;
using Fodinha.Game.Menu;
using Fodinha.Hosting;
using Fodinha.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fodinha.Online
{
    /// <summary>Raiz da cena da sala: liga a <see cref="LobbyScreen"/> ao <see cref="OnlineClient"/>.</summary>
    public sealed class LobbyRoot : MonoBehaviour
    {
        [SerializeField] private LobbyScreen screen;
        private OnlineClient client;

        public LobbyScreen Screen => screen;

        public void Configure(LobbyScreen lobbyScreen) => screen = lobbyScreen;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            if (screen == null) screen = FindAnyObjectByType<LobbyScreen>();
            client = OnlineClient.Instance;

            screen.LeaveRequested += Leave;
            screen.AddBotRequested += () => client.Send(Protocol.ToServer.AddBot);
            screen.RemoveBotRequested += i => client.Send(Protocol.ToServer.RemoveBot, new RemoveBotMsg { index = i });
            screen.StartRequested += () => client.Send(Protocol.ToServer.StartMatch);
            screen.OptionsSubmitted += o => client.Send(Protocol.ToServer.UpdateOptions, new UpdateOptionsMsg { options = o });

            client.RoomStateReceived += OnRoom;
            client.LeftRoom += OnLeft;
            client.ErrorReceived += OnError;
            client.StateChanged += OnState;
        }

        private void Start()
        {
            if (client.Room == null)
            {
                SceneManager.LoadScene(SceneNames.Menu);
                return;
            }
            OnRoom(client.Room);
            screen.SetConnected(client.IsConnected || client.State != OnlineClient.ConnectionState.Offline);
        }

        private void OnDestroy()
        {
            if (client == null) return;
            client.RoomStateReceived -= OnRoom;
            client.LeftRoom -= OnLeft;
            client.ErrorReceived -= OnError;
            client.StateChanged -= OnState;
        }

        private void OnRoom(RoomStateDto room)
        {
            if (room.inMatch)
            {
                SceneManager.LoadScene(SceneNames.OnlineMatch);
                return;
            }
            screen.Render(room);
        }

        private void OnLeft(string reason)
        {
            if (!string.IsNullOrEmpty(reason) && reason != "saiu") MenuRoot.PendingToast = "Você saiu da sala (" + reason + ").";
            SceneManager.LoadScene(SceneNames.Menu);
        }

        private void OnError(ErrorMsg e) => screen.ShowToast(OnlineClient.Describe(e));

        private void OnState(OnlineClient.ConnectionState s) => screen.SetConnected(s != OnlineClient.ConnectionState.Offline);

        private void Leave()
        {
            client.LeaveRoom();
            SceneManager.LoadScene(SceneNames.Menu);
        }
    }
}
