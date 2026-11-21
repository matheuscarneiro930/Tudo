using Fodinha.Core;
using Fodinha.Game;
using Fodinha.Game.Menu;
using Fodinha.Game.UI;
using Fodinha.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fodinha.Online
{
    /// <summary>
    /// Raiz da cena da partida online: a mesma <see cref="MatchScreen"/> do offline, ligada a uma
    /// <see cref="NetworkMatchSession"/> alimentada pelo servidor.
    /// </summary>
    public sealed class OnlineGameRoot : MonoBehaviour
    {
        [SerializeField] private MatchScreen screen;
        private OnlineClient client;
        private NetworkMatchSession session;
        private float nextConnectionToast;

        public MatchScreen Screen => screen;
        public NetworkMatchSession Session => session;

        public void Configure(MatchScreen matchScreen) => screen = matchScreen;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            UnityEngine.Screen.sleepTimeout = SleepTimeout.NeverSleep;
            if (screen == null) screen = FindAnyObjectByType<MatchScreen>();

            client = OnlineClient.Instance;
            session = new NetworkMatchSession(client);
            screen.Bind(session);
            screen.SetPlayAgainLabel("Voltar à sala");
            screen.PlayAgainRequested += BackToLobby;
            screen.MenuRequested += OnMenuRequested;

            client.MatchReceived += session.Apply;
            client.ErrorReceived += OnError;
            client.LeftRoom += OnLeft;
            client.ProfileReceived += OnProfile;
        }

        private void Start()
        {
            if (client.Room == null)
            {
                SceneManager.LoadScene(SceneNames.Menu);
                return;
            }
            if (client.LastMatch != null) session.Apply(client.LastMatch);
        }

        private void OnDestroy()
        {
            if (client == null) return;
            client.MatchReceived -= session.Apply;
            client.ErrorReceived -= OnError;
            client.LeftRoom -= OnLeft;
            client.ProfileReceived -= OnProfile;
        }

        private void Update()
        {
            if (client.State == OnlineClient.ConnectionState.Offline && Time.unscaledTime > nextConnectionToast)
            {
                nextConnectionToast = Time.unscaledTime + 3f;
                screen.ShowToast("Conexão perdida. Reconectando... (o bot joga por você)", 2.8f);
            }
        }

        /// <summary>Moedas da partida: entrada paga, prêmio do vencedor ou devolução.</summary>
        private void OnProfile(ProfileMsg p)
        {
            if (p.delta == 0) return;
            switch (p.reason)
            {
                case "entrada": screen.ShowToast("Entrada: " + p.delta + " moedas (saldo " + p.coins + ")", 3f); break;
                case "vitoria": screen.ShowToast("Você ganhou o pote: +" + p.delta + " moedas!", 5f); break;
                case "reembolso": screen.ShowToast("Partida encerrada: +" + p.delta + " moedas devolvidas", 4f); break;
            }
        }

        private void OnError(ErrorMsg e)
        {
            if (e.code == ErrorCodes.Action) session.ApplyError(e);
            else screen.ShowToast(OnlineClient.Describe(e));
        }

        private void OnLeft(string reason)
        {
            if (!string.IsNullOrEmpty(reason) && reason != "saiu") MenuRoot.PendingToast = "A partida foi encerrada (" + reason + ").";
            SceneManager.LoadScene(SceneNames.Menu);
        }

        private void BackToLobby()
        {
            SceneManager.LoadScene(client.Room != null ? SceneNames.Lobby : SceneNames.Menu);
        }

        private void OnMenuRequested()
        {
            bool over = session.View != null && session.View.Phase == MatchPhase.MatchOver;
            if (over)
            {
                client.LeaveRoom();
                SceneManager.LoadScene(SceneNames.Menu);
                return;
            }
            UiControls.Confirm(screen.transform, "Sair da partida?",
                               "Você será eliminado ao fim desta rodada (o bot joga por você até lá).", "Sair", () =>
                               {
                                   client.LeaveRoom();
                                   SceneManager.LoadScene(SceneNames.Menu);
                               }, "Continuar");
        }
    }
}
