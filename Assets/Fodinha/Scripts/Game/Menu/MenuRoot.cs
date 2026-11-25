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

        /// <summary>Aviso para mostrar quando o menu abrir (ex.: "a sala foi encerrada").</summary>
        public static string PendingToast;

        public MenuScreen Screen => screen;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            if (screen == null) screen = FindAnyObjectByType<MenuScreen>();
            if (!string.IsNullOrEmpty(PendingToast))
            {
                screen.ShowToast(PendingToast, 4f);
                PendingToast = null;
            }

            screen.OfflineRequested += StartOffline;
            screen.CreateRoomRequested += options => WithOnline(h => h.CreateRoom(screen, options));
            screen.JoinRoomRequested += code => WithOnline(h => h.JoinRoom(screen, code));
            screen.RefreshRoomsRequested += () => WithOnline(h => h.RefreshPublicRooms(screen), quiet: true);
            screen.RankingRequested += () => WithOnline(h => h.ShowRanking(screen));
            screen.ClaimRewardRequested += () => WithOnline(h => h.ClaimReward(screen));
            screen.AvatarSaved += avatar => WithOnline(h => h.SaveAvatar(screen, avatar), quiet: true);
            screen.BuyRequested += item => WithOnline(h => h.BuyItem(screen, item));
            screen.EquipRequested += item => WithOnline(h => h.EquipItem(screen, item), quiet: true);
            screen.VipCodeSubmitted += code => WithOnline(h => h.RedeemCode(screen, code));
            screen.InviteIosRequested += InviteIos;
            OnlineHandler?.OnMenuOpened(screen);
        }

        public void Configure(MenuScreen menuScreen, string offlineSceneName)
        {
            screen = menuScreen;
            offlineScene = offlineSceneName;
        }

        /// <summary>Compartilha o link do site (WhatsApp etc.). Se o aparelho só puder copiar, avisa.</summary>
        private void InviteIos()
        {
            var result = ShareService.Share(GamePrefs.IosInviteMessage);
            if (result == ShareService.Result.Copied)
                screen.ShowToast("Convite copiado! Cole no WhatsApp do seu amigo.", 3.5f);
            else if (result == ShareService.Result.Failed)
                screen.ShowToast("Mande este link para seu amigo: " + GamePrefs.SiteUrl, 6f);
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
        /// <summary>O menu abriu (ex.: acordar o servidor com antecedência).</summary>
        void OnMenuOpened(MenuScreen screen);

        void CreateRoom(MenuScreen screen, RoomOptions options);
        void JoinRoom(MenuScreen screen, string code);
        void RefreshPublicRooms(MenuScreen screen);
        void ShowRanking(MenuScreen screen);
        /// <summary>Assistir um vídeo e ganhar moedas (só com menos de 500).</summary>
        void ClaimReward(MenuScreen screen);
        /// <summary>Avatar mudou: avisa o servidor (os outros jogadores passam a ver o novo).</summary>
        void SaveAvatar(MenuScreen screen, string avatar);
        /// <summary>Comprar um item da loja com moedas (o servidor confere o saldo).</summary>
        void BuyItem(MenuScreen screen, string itemId);
        /// <summary>Usar um item já comprado.</summary>
        void EquipItem(MenuScreen screen, string itemId);
        /// <summary>Código VIP: conta sem anúncios.</summary>
        void RedeemCode(MenuScreen screen, string code);
    }
}
