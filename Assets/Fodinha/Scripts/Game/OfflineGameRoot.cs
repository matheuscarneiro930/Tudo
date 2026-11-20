using Fodinha.Game.UI;
using UnityEngine;

namespace Fodinha.Game
{
    /// <summary>
    /// Raiz da cena de partida offline: liga a tela (<see cref="MatchScreen"/>) ao <see cref="LocalMatchController"/>
    /// e trata "jogar de novo" / "menu".
    /// </summary>
    public sealed class OfflineGameRoot : MonoBehaviour
    {
        /// <summary>Nome da cena do menu principal (etapa 3). Vazio = sem menu.</summary>
        public static string MenuSceneName = "";

        [SerializeField] private LocalMatchController controller;
        [SerializeField] private MatchScreen screen;

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
            if (!string.IsNullOrEmpty(MenuSceneName))
                screen.MenuRequested += () => UnityEngine.SceneManagement.SceneManager.LoadScene(MenuSceneName);
        }

        /// <summary>Usado pelo construtor de cena no Editor para ligar as referências.</summary>
        public void Configure(LocalMatchController matchController, MatchScreen matchScreen)
        {
            controller = matchController;
            screen = matchScreen;
        }
    }
}
