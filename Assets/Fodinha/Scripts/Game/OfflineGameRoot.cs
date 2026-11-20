using Fodinha.Game.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fodinha.Game
{
    /// <summary>
    /// Raiz da cena de partida offline: liga a tela (<see cref="MatchScreen"/>) ao <see cref="LocalMatchController"/>
    /// e trata "jogar de novo" / "voltar ao menu".
    /// </summary>
    public sealed class OfflineGameRoot : MonoBehaviour
    {
        [SerializeField] private LocalMatchController controller;
        [SerializeField] private MatchScreen screen;

        [Tooltip("Cena do menu principal. Vazio = sem botão de menu.")]
        [SerializeField] private string menuScene = "Menu";

        public LocalMatchController Controller => controller;
        public MatchScreen Screen => screen;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            UnityEngine.Screen.sleepTimeout = SleepTimeout.NeverSleep;

            if (controller == null) controller = FindAnyObjectByType<LocalMatchController>();
            if (screen == null) screen = FindAnyObjectByType<MatchScreen>();

            screen.Bind(controller);
            screen.PlayAgainRequested += controller.Restart;
            if (!string.IsNullOrEmpty(menuScene) && Application.CanStreamedLevelBeLoaded(menuScene))
                screen.MenuRequested += OnMenuRequested;
        }

        private void OnMenuRequested()
        {
            bool over = controller.View != null && controller.View.Phase == Core.MatchPhase.MatchOver;
            if (over)
            {
                SceneManager.LoadScene(menuScene);
                return;
            }
            UiControls.Confirm(screen.transform, "Sair da partida?", "A partida em andamento será perdida.",
                               "Sair", () => SceneManager.LoadScene(menuScene), "Continuar");
        }

        /// <summary>Usado pelo construtor de cena no Editor para ligar as referências.</summary>
        public void Configure(LocalMatchController matchController, MatchScreen matchScreen, string menuSceneName)
        {
            controller = matchController;
            screen = matchScreen;
            menuScene = menuSceneName;
        }
    }
}
