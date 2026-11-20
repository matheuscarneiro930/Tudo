using System;
using System.Collections;
using System.Collections.Generic;
using Fodinha.Game;
using Fodinha.Game.Menu;
using Fodinha.Game.UI;
using Fodinha.Hosting;
using Fodinha.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fodinha.Online
{
    /// <summary>Liga os botões online do menu (criar sala, entrar com código, salas públicas) ao servidor.</summary>
    public sealed class OnlineMenuHandler : IOnlineMenuHandler
    {
        private const float TimeoutSeconds = OnlineClient.ConnectTimeoutSeconds + 15f;

        public void OnMenuOpened(MenuScreen screen) => OnlineClient.Instance.WakeUpServer();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            MenuRoot.OnlineHandler = new OnlineMenuHandler();
        }

        public void CreateRoom(MenuScreen screen, RoomOptions options) =>
            RunRoomOperation(screen, "Criando a sala...", c => c.Send(Protocol.ToServer.CreateRoom, new CreateRoomMsg { options = options }));

        public void JoinRoom(MenuScreen screen, string code) =>
            RunRoomOperation(screen, "Entrando na sala " + code + "...", c => c.Send(Protocol.ToServer.JoinRoom, new JoinRoomMsg { code = code }));

        public void RefreshPublicRooms(MenuScreen screen)
        {
            var client = OnlineClient.Instance;
            Action<List<RoomListing>> onList = null;
            Action<string> onFail = null;
            void Cleanup()
            {
                client.RoomListReceived -= onList;
                client.ConnectFailed -= onFail;
            }
            onList = rooms =>
            {
                Cleanup();
                if (screen != null) screen.SetPublicRooms(rooms);
            };
            onFail = message =>
            {
                Cleanup();
                if (screen == null) return;
                screen.SetPublicRooms(Array.Empty<RoomListing>());
                screen.ShowToast(message + " Verifique a internet.");
            };
            client.RoomListReceived += onList;
            client.ConnectFailed += onFail;
            client.Send(Protocol.ToServer.ListRooms);
        }

        /// <summary>Envia o pedido e espera a sala (vai para o lobby), um erro (avisa) ou a falha de conexão.</summary>
        private static void RunRoomOperation(MenuScreen screen, string busyMessage, Action<OnlineClient> send)
        {
            var client = OnlineClient.Instance;
            bool done = false;
            Action<RoomStateDto> onRoom = null;
            Action<ErrorMsg> onError = null;
            Action<string> onFail = null;

            void Finish(string toast)
            {
                done = true;
                client.RoomStateReceived -= onRoom;
                client.ErrorReceived -= onError;
                client.ConnectFailed -= onFail;
                if (screen == null) return;
                screen.SetBusy(false);
                if (toast != null) screen.ShowToast(toast);
            }

            onRoom = room =>
            {
                Finish(null);
                SceneManager.LoadScene(room.inMatch ? SceneNames.OnlineMatch : SceneNames.Lobby);
            };
            onError = e =>
            {
                if (e.code == ErrorCodes.Action) return;
                Finish(OnlineClient.Describe(e));
            };
            onFail = message => Finish(message + " Verifique a internet.");

            client.RoomStateReceived += onRoom;
            client.ErrorReceived += onError;
            client.ConnectFailed += onFail;

            if (screen != null)
                screen.SetBusy(true, client.IsConnected ? busyMessage
                    : busyMessage + "\n\n" + UiKit.Colored("Conectando ao servidor (se ele estava dormindo, pode levar até 1 minuto)", Palette.TextMuted));
            send(client);
            client.StartCoroutine(Timeout(() =>
            {
                if (!done) Finish("O servidor não respondeu. Tente de novo.");
            }));
        }

        private static IEnumerator Timeout(Action onTimeout)
        {
            yield return new WaitForSecondsRealtime(TimeoutSeconds);
            onTimeout();
        }
    }
}
