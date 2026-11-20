using UnityEngine;
using UnityEngine.UI;

namespace Fodinha.Game.UI
{
    /// <summary>Barra de tempo: encolhe da direita para a esquerda e vai do verde ao vermelho.</summary>
    public sealed class TimerBar : MonoBehaviour
    {
        private Image fill;
        private RectTransform fillRect;

        public static TimerBar Create(Transform parent, string name = "Tempo")
        {
            var bg = UiKit.Image(name, parent, new Color(0f, 0f, 0f, 0.55f), SpriteFactory.RoundedRect, 6f);
            bg.raycastTarget = false;
            var bar = bg.gameObject.AddComponent<TimerBar>();
            bar.fill = UiKit.Image("Preenchimento", bg.transform, Palette.Good, SpriteFactory.RoundedRect, 6f);
            bar.fill.raycastTarget = false;
            bar.fillRect = bar.fill.rectTransform;
            bar.fillRect.Fill();
            bar.SetFraction(1f);
            return bar;
        }

        public void SetFraction(float fraction)
        {
            fraction = Mathf.Clamp01(fraction);
            fillRect.anchorMax = new Vector2(fraction, 1f);
            fill.color = fraction > 0.5f
                ? Color.Lerp(Palette.Accent, Palette.Good, (fraction - 0.5f) * 2f)
                : Color.Lerp(Palette.Bad, Palette.Accent, fraction * 2f);
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }
    }
}
