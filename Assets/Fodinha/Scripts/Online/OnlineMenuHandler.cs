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

        private MenuScreen current;
        private bool subscribed;

        /// <summary>Menu aberto: conecta, busca o saldo e mantém a carteira e o ranking atualizados na tela.</summary>
        public void OnMenuOpened(MenuScreen screen)
        {
            current = screen;
            var client = OnlineClient.Instance;
            if (!subscribed)
            {
                subscribed = true;
                client.ProfileReceived += OnProfile;
                client.RankingReceived += OnRanking;
                client.ConnectFailed += _ => { if (current != null) current.SetCoins(null, false); };
                client.ErrorReceived += e =>
                {
                    if (current == null) return;
                    if (e.code == ErrorCodes.RewardUnavailable || e.code == ErrorCodes.Shop || e.code == ErrorCodes.Code ||
                        e.code == ErrorCodes.TooMany ||
                        (e.code == ErrorCodes.NotEnoughCoins && current.CurrentPage == MenuScreen.Page.Shop))
                        current.ShowToast(OnlineClient.Describe(e), 3f);
                };
            }
            if (client.Profile != null) screen.SetCoins(client.Profile.coins, client.Profile.canClaimReward);
            client.WakeUpServer();
            client.Send(Protocol.ToServer.GetProfile);
        }

        private void OnProfile(ProfileMsg p)
        {
            // Itens, mesa, baralho e "sem anúncios" valem também no modo offline: ficam guardados no aparelho.
            GamePrefs.ApplyServerProfile(p.owned, p.table, p.deck, p.noAds);
            Game.Ads.AdService.RequireRealAd = p.rewardNeedsApp;
            if (current == null) return;
            current.SetCoins(p.coins, p.canClaimReward);
            current.RefreshCosmetics();
            if (p.delta > 0 && p.reason == "recompensa") current.ShowToast("+" + p.delta + " moedas!");
            else if (p.reason == "compra") current.ShowToast("Compra feita! Já está em uso.");
            else if (p.reason == "vip") current.ShowToast("Código aceito! Sua conta está sem anúncios.", 3.5f);
        }

        public void SaveAvatar(MenuScreen screen, string avatar)
        {
            current = screen;
            OnlineClient.Instance.Send(Protocol.ToServer.SetAvatar, new AvatarMsg { avatar = avatar });
        }

        public void BuyItem(MenuScreen screen, string itemId)
        {
            current = screen;
            OnlineClient.Instance.Send(Protocol.ToServer.BuyItem, new ItemMsg { item = itemId });
        }

        public void EquipItem(MenuScreen screen, string itemId)
        {
            current = screen;
            OnlineClient.Instance.Send(Protocol.ToServer.EquipItem, new ItemMsg { item = itemId });
        }

        public void RedeemCode(MenuScreen screen, string code)
        {
            current = screen;
            OnlineClient.Instance.Send(Protocol.ToServer.RedeemCode, new CodeMsg { code = code });
        }

        private void OnRanking(RankingMsg r)
        {
            if (current == null) return;
            var rows = new List<MenuScreen.RankingRow>();
            foreach (var e in r.top)
                rows.Add(new MenuScreen.RankingRow { Position = e.position, Name = e.name, Coins = e.coins, Wins = e.wins });
            current.ShowRanking(rows, r.myPosition, r.myCoins);
        }

        public void ShowRanking(MenuScreen screen)
        {
            current = screen;
            OnlineClient.Instance.Send(Protocol.ToServer.GetRanking);
        }

        /// <summary>
        /// "Assista e ganhe 100 moedas": mostra o vídeo premiado (AdMob no celular) e, se a pessoa assistiu até o fim,
        /// pede a recompensa ao servidor, que confere o saldo (menos de 500) e o intervalo.
        /// </summary>
        public void ClaimReward(MenuScreen screen)
        {
            current = screen;
            // Conta sem anúncios (código VIP): ganha direto, sem vídeo. O servidor continua conferindo o saldo.
            if (GamePrefs.NoAds)
            {
                OnlineClient.Instance.Send(Protocol.ToServer.ClaimReward);
                return;
            }
            // Com a verificação do Google ligada, só vale vídeo de verdade (o do app de celular).
            if (Game.Ads.AdService.RequireRealAd && !Game.Ads.AdService.HasRealAds)
            {
                screen.ShowToast("O vídeo de +100 moedas funciona no app de celular.", 3.5f);
                return;
            }
            Game.Ads.AdService.ShowRewarded(earned =>
            {
                if (earned)
                {
                    OnlineClient.Instance.Send(Protocol.ToServer.ClaimReward);
                    // Verificado pelo Google: as moedas chegam quando o aviso dele bate no servidor (segundos).
                    if (Game.Ads.AdService.RequireRealAd && current != null)
                        current.ShowToast("Vídeo assistido! Suas moedas chegam em instantes.", 3f);
                }
                else if (current != null) current.ShowToast("O vídeo não está disponível agora. Tente de novo em instantes.");
            });
        }

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
