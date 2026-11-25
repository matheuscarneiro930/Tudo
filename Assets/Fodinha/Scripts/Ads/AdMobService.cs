using System;
using Fodinha.Game.Ads;
using GoogleMobileAds.Api;
using GoogleMobileAds.Ump.Api;
using UnityEngine;

namespace Fodinha.Ads
{
    /// <summary>
    /// Anúncios do AdMob no celular: consentimento (UMP), vídeo premiado e tela cheia no fim da partida.
    /// Os anúncios são pré-carregados e recarregados depois de exibidos.
    /// </summary>
    public sealed class AdMobService : IAdService
    {
        private RewardedAd rewarded;
        private InterstitialAd interstitial;
        private bool started;
        private bool loadingRewarded;
        private bool loadingInterstitial;

        public string Name => AdIds.UsingTestAds ? "AdMob (teste)" : "AdMob";

        /// <summary>Registra o AdMob como serviço de anúncios no celular, antes da primeira cena.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            AdService.Current = new AdMobService();
#endif
        }

        /// <summary>Depois que a primeira tela abre: consentimento e pré-carregamento dos anúncios.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartAfterFirstScene()
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            AdService.EnsureInitialized();
#endif
        }

        public void Initialize()
        {
            MobileAds.RaiseAdEventsOnUnityMainThread = true;

            // Consentimento: o Google mostra o formulário só onde a lei exige; depois disso pode pedir anúncios.
            var request = new ConsentRequestParameters();
            ConsentInformation.Update(request, updateError =>
            {
                if (updateError != null) Debug.Log("[Fodinha] Consentimento: " + updateError.Message);
                ConsentForm.LoadAndShowConsentFormIfRequired(formError =>
                {
                    if (formError != null) Debug.Log("[Fodinha] Formulário de consentimento: " + formError.Message);
                    if (ConsentInformation.CanRequestAds()) Start();
                });
            });
            if (ConsentInformation.CanRequestAds()) Start();
        }

        private void Start()
        {
            if (started) return;
            started = true;
            MobileAds.Initialize(_ =>
            {
                LoadRewarded();
                LoadInterstitial();
            });
        }

        private void LoadRewarded()
        {
            if (loadingRewarded) return;
            loadingRewarded = true;
            RewardedAd.Load(AdIds.Rewarded, new AdRequest(), (ad, error) =>
            {
                loadingRewarded = false;
                if (error != null || ad == null)
                {
                    Debug.Log("[Fodinha] Vídeo premiado não carregou: " + error?.GetMessage());
                    return;
                }
                rewarded = ad;
            });
        }

        private void LoadInterstitial()
        {
            if (loadingInterstitial) return;
            loadingInterstitial = true;
            InterstitialAd.Load(AdIds.Interstitial, new AdRequest(), (ad, error) =>
            {
                loadingInterstitial = false;
                if (error != null || ad == null)
                {
                    Debug.Log("[Fodinha] Anúncio de tela cheia não carregou: " + error?.GetMessage());
                    return;
                }
                interstitial = ad;
            });
        }

        public void ShowRewarded(Action<bool> onFinished)
        {
            if (rewarded == null || !rewarded.CanShowAd())
            {
                LoadRewarded();
                // Com a verificação do Google ligada, a tela provisória não vale nada: avisa que não há vídeo.
                if (AdService.RequireRealAd) { onFinished(false); return; }
                // Sem vídeo disponível (comum antes do app estar na loja): não deixa o jogador preso sem moedas.
                // O servidor continua limitando a recompensa (só abaixo de 500, com intervalo e limite diário).
                new PlaceholderAdService().ShowRewarded(onFinished);
                return;
            }

            var ad = rewarded;
            rewarded = null;
            // Verificação no servidor (SSV): o Google avisa o nosso servidor que ESTE jogador assistiu.
            if (!string.IsNullOrEmpty(AdService.RewardUserId))
                ad.SetServerSideVerificationOptions(new ServerSideVerificationOptions { UserId = AdService.RewardUserId });
            bool earned = false;
            bool finished = false;
            void Finish()
            {
                if (finished) return;
                finished = true;
                ad.Destroy();
                LoadRewarded();
                onFinished(earned);
            }
            ad.OnAdFullScreenContentClosed += Finish;
            ad.OnAdFullScreenContentFailed += _ => Finish();
            ad.Show(_ => earned = true);
        }

        public void ShowInterstitial(Action onClosed)
        {
            if (interstitial == null || !interstitial.CanShowAd())
            {
                LoadInterstitial();
                onClosed();
                return;
            }

            var ad = interstitial;
            interstitial = null;
            bool finished = false;
            void Finish()
            {
                if (finished) return;
                finished = true;
                ad.Destroy();
                LoadInterstitial();
                onClosed();
            }
            ad.OnAdFullScreenContentClosed += Finish;
            ad.OnAdFullScreenContentFailed += _ => Finish();
            ad.Show();
        }
    }
}
