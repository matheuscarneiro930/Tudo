using System;
using System.Collections;
using Fodinha.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Fodinha.Game.Ads
{
    /// <summary>Anúncios do jogo. No celular é o AdMob (assembly Fodinha.Ads); no site/PC, uma tela provisória.</summary>
    public interface IAdService
    {
        string Name { get; }

        /// <summary>Consentimento (LGPD/GDPR) e carregamento inicial. Chamado uma vez ao abrir o app.</summary>
        void Initialize();

        /// <summary>Vídeo premiado. <paramref name="onFinished"/> recebe true se a pessoa assistiu até ganhar a recompensa.</summary>
        void ShowRewarded(Action<bool> onFinished);

        /// <summary>Anúncio de tela cheia (fim de partida). Sempre chama <paramref name="onClosed"/>, mesmo sem anúncio.</summary>
        void ShowInterstitial(Action onClosed);
    }

    /// <summary>Ponto único de acesso aos anúncios.</summary>
    public static class AdService
    {
        private static IAdService current;
        private static bool initialized;

        /// <summary>Serviço atual. O AdMob se registra sozinho no celular; senão, a tela provisória.</summary>
        public static IAdService Current
        {
            get => current ?? (current = new PlaceholderAdService());
            set
            {
                current = value;
                initialized = false;
            }
        }

        /// <summary>Quantos anúncios de fim de partida foram pedidos (testes).</summary>
        public static int InterstitialsRequested { get; private set; }

        public static void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            try { Current.Initialize(); }
            catch (Exception e) { Debug.LogWarning("[Fodinha] Anúncios indisponíveis: " + e.Message); }
        }

        public static void ShowRewarded(Action<bool> onFinished)
        {
            EnsureInitialized();
            Current.ShowRewarded(onFinished ?? (_ => { }));
        }

        /// <summary>Fim de partida: mostra o anúncio de tela cheia (no celular).</summary>
        public static void ShowInterstitial(Action onClosed = null)
        {
            InterstitialsRequested++;
            EnsureInitialized();
            Current.ShowInterstitial(onClosed ?? (() => { }));
        }
    }

    /// <summary>
    /// Site e PC: sem rede de anúncios. O vídeo premiado é uma tela de espera de alguns segundos;
    /// o anúncio de fim de partida não aparece (para não atrapalhar quem joga no navegador).
    /// </summary>
    public sealed class PlaceholderAdService : IAdService
    {
        public const float RewardedSeconds = 5f;

        public string Name => "provisório";

        public void Initialize() { }

        public void ShowRewarded(Action<bool> onFinished)
        {
            if (!Application.isPlaying) { onFinished(true); return; }
            AdOverlay.Run(RewardedSeconds, () => onFinished(true));
        }

        public void ShowInterstitial(Action onClosed) => onClosed();
    }

    /// <summary>Tela "Anúncio" com contagem regressiva, por cima de tudo.</summary>
    internal sealed class AdOverlay : MonoBehaviour
    {
        public static void Run(float seconds, Action done)
        {
            var go = new GameObject("Anuncio");
            DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            go.AddComponent<GraphicRaycaster>();

            var bg = UiKit.Image("Fundo", go.transform, new Color(0f, 0f, 0f, 0.92f));
            bg.rectTransform.Fill();
            var text = UiKit.Label("Texto", bg.transform, "", 52, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            text.rectTransform.Anchor(0.08f, 0.35f, 0.92f, 0.65f);

            var overlay = go.AddComponent<AdOverlay>();
            overlay.StartCoroutine(overlay.Countdown(seconds, text, done));
        }

        private IEnumerator Countdown(float seconds, Text text, Action done)
        {
            for (float t = seconds; t > 0f; t -= Time.unscaledDeltaTime)
            {
                text.text = "Anúncio\n\n" + UiKit.Colored("Ganhando 100 moedas em " + Mathf.CeilToInt(t) + "...", Palette.TextMuted);
                yield return null;
            }
            Destroy(gameObject);
            done();
        }
    }
}
