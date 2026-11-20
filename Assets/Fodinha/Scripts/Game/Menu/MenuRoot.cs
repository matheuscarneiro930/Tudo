using System;
using Fodinha.Hosting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fodinha.Game.Menu
{
    /// <summary>
    /// Raiz da cena do menu: executa os pedidos do <see cref="MenuScreen"/>.
    /// Offline: guarda a configuração e abre a cena da partida.
    /// Online: repassa para o serviço de salas (<see cref="IOnlineMenuHandler"/>), quando existir.
    /// </summary>
    public sealed class MenuRoot : MonoBehaviour
    {
        [SerializeField] private MenuScreen screen;
        [SerializeField] private string offlineScene = "PartidaOffline";

        /// <summary>Carrega uma cena pelo nome (substituível nos testes).</summary>
        public Action<string> SceneLoader = name => SceneManager.LoadScene(name);

        /// <summary>Quem trata criar/entrar/listar salas. Registrado pela camada online.</summary>
        public static IOnlineMenuHandler OnlineHandler;

        public MenuScreen Screen => screen;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            if (screen == null) screen = FindAnyObjectByType<MenuScreen>();

            screen.OfflineRequested += StartOffline;
            screen.CreateRoomRequested += options => WithOnline(h => h.CreateRoom(screen, options));
            screen.JoinRoomRequested += code => WithOnline(h => h.JoinRoom(screen, code));
            screen.RefreshRoomsRequested += () => WithOnline(h => h.RefreshPublicRooms(screen), quiet: true);
        }

        public void Configure(MenuScreen menuScreen, string offlineSceneName)
        {
            screen = menuScreen;
            offlineScene = offlineSceneName;
        }

        private void StartOffline(OfflineMatchConfig config)
        {
            LocalMatchController.PendingConfig = config;
            SceneLoader(offlineScene);
        }

        private void WithOnline(Action<IOnlineMenuHandler> action, bool quiet = false)
        {
            if (OnlineHandler == null)
            {
                screen.SetPublicRooms(Array.Empty<RoomListing>());
                if (!quiet) screen.ShowToast("O modo online ainda não está disponível nesta versão.");
                return;
            }
            action(OnlineHandler);
        }
    }

    /// <summary>Ações online disparadas pelo menu (implementado pela camada de rede).</summary>
    public interface IOnlineMenuHandler
    {
        void CreateRoom(MenuScreen screen, RoomOptions options);
        void JoinRoom(MenuScreen screen, string code);
        void RefreshPublicRooms(MenuScreen screen);
    }
}
