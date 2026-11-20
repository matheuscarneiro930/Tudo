using System;
using Fodinha.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Fodinha.Game.UI
{
    /// <summary>
    /// Uma carta desenhada por código: frente (valor + naipe), verso, ou "?" (a própria carta na rodada cega).
    /// Todo o conteúdo usa âncoras relativas, então a carta funciona em qualquer tamanho.
    /// </summary>
    public sealed class CardView : MonoBehaviour, IPointerClickHandler
    {
        /// <summary>Proporção largura/altura das cartas.</summary>
        public const float Aspect = 0.69f;

        /// <summary>
        /// Tamanho máximo das letras na carta. Fontes muito grandes não cabem na textura de fontes do navegador/celular
        /// (as letras somem), então o limite é moderado. O gerador de ícone (Editor) aumenta via <see cref="AllowLargeText"/>.
        /// </summary>
        public const int MaxRankSize = 150;
        public const int MaxSuitSize = 300;

        /// <summary>Só para renderizações grandes no Editor (ícone): deixa o texto crescer mais.</summary>
        public void AllowLargeText(float factor)
        {
            foreach (var t in GetComponentsInChildren<Text>(true))
                if (t.resizeTextForBestFit) t.resizeTextMaxSize = Mathf.RoundToInt(t.resizeTextMaxSize * factor);
        }

        private Image shadow;
        private Image body;
        private Image backImage;
        private Image outline;
        private Text cornerRank;
        private Text cornerSuit;
        private Text centerSuit;
        private Text bottomRank;
        private Text manilhaTag;
        private Text question;
        private CanvasGroup group;

        public RectTransform Rect { get; private set; }
        public Card? Card { get; private set; }
        public event Action<CardView> Clicked;

        public static CardView Create(Transform parent, string name = "Carta")
        {
            var rt = UiKit.Rect(name, parent);
            var view = rt.gameObject.AddComponent<CardView>();
            view.Build(rt);
            return view;
        }

        private void Build(RectTransform rt)
        {
            Rect = rt;
            group = gameObject.AddComponent<CanvasGroup>();

            shadow = UiKit.Image("Sombra", rt, new Color(0f, 0f, 0f, 0.35f), SpriteFactory.RoundedRect, 3f);
            shadow.rectTransform.Fill();
            shadow.rectTransform.offsetMin = new Vector2(-2f, -6f);
            shadow.rectTransform.offsetMax = new Vector2(4f, 0f);
            shadow.raycastTarget = false;

            body = UiKit.Image("Frente", rt, Palette.CardFace, SpriteFactory.RoundedRect, 2.5f);
            body.rectTransform.Fill();

            cornerRank = UiKit.Label("Valor", body.transform, "", MaxRankSize, Palette.BlackSuit, TextAnchor.UpperLeft, FontStyle.Bold, true);
            cornerRank.rectTransform.Anchor(0.07f, 0.66f, 0.6f, 0.97f);
            cornerSuit = UiKit.Label("NaipeCanto", body.transform, "", MaxRankSize, Palette.BlackSuit, TextAnchor.UpperLeft, FontStyle.Normal, true);
            cornerSuit.rectTransform.Anchor(0.08f, 0.47f, 0.45f, 0.68f);
            centerSuit = UiKit.Label("NaipeCentro", body.transform, "", MaxSuitSize, Palette.BlackSuit, TextAnchor.MiddleCenter, FontStyle.Normal, true);
            centerSuit.rectTransform.Anchor(0.25f, 0.08f, 0.95f, 0.62f);
            bottomRank = UiKit.Label("ValorBaixo", body.transform, "", MaxRankSize, Palette.BlackSuit, TextAnchor.LowerRight, FontStyle.Bold, true);
            bottomRank.rectTransform.Anchor(0.5f, 0.03f, 0.93f, 0.25f);

            backImage = UiKit.Image("Verso", rt, Color.white, SpriteFactory.CardBack);
            backImage.rectTransform.Fill();
            backImage.preserveAspect = false;

            question = UiKit.Label("Interrogacao", backImage.transform, "?", MaxSuitSize, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            question.rectTransform.Anchor(0.1f, 0.15f, 0.9f, 0.85f);
            var qOutline = question.gameObject.AddComponent<Outline>();
            qOutline.effectColor = new Color(0f, 0f, 0f, 0.6f);
            qOutline.effectDistance = new Vector2(2f, -2f);

            manilhaTag = UiKit.Label("Manilha", rt, "MANILHA", 60, Palette.AccentDark, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            var tagBg = UiKit.Image("FundoManilha", rt, Palette.Manilha, SpriteFactory.RoundedRect, 6f);
            tagBg.raycastTarget = false;
            tagBg.rectTransform.Anchor(0.08f, -0.06f, 0.92f, 0.08f);
            manilhaTag.transform.SetParent(tagBg.transform, false);
            manilhaTag.rectTransform.Anchor(0.04f, 0.05f, 0.96f, 0.95f);

            outline = UiKit.Image("Contorno", rt, Palette.Accent, SpriteFactory.RoundedOutline, 2.5f);
            outline.rectTransform.Fill();
            outline.rectTransform.offsetMin = new Vector2(-5f, -5f);
            outline.rectTransform.offsetMax = new Vector2(5f, 5f);
            outline.raycastTarget = false;

            ShowBack();
        }

        public void ShowFace(Card card, bool isManilha)
        {
            Card = card;
            bool red = card.Suit == Suit.Hearts || card.Suit == Suit.Diamonds;
            var color = red ? Palette.RedSuit : Palette.BlackSuit;
            string rank = card.Rank.Symbol();
            string suit = card.Suit.Symbol();

            body.gameObject.SetActive(true);
            backImage.gameObject.SetActive(false);
            cornerRank.text = rank;
            cornerSuit.text = suit;
            centerSuit.text = suit;
            bottomRank.text = rank;
            cornerRank.color = cornerSuit.color = centerSuit.color = bottomRank.color = color;
            body.color = isManilha ? Palette.Hex("FFF4D6") : Palette.CardFace;
            manilhaTag.transform.parent.gameObject.SetActive(isManilha);
        }

        public void ShowBack()
        {
            Card = null;
            body.gameObject.SetActive(false);
            backImage.gameObject.SetActive(true);
            question.gameObject.SetActive(false);
            manilhaTag.transform.parent.gameObject.SetActive(false);
        }

        /// <summary>Carta virada para baixo com "?" (a própria carta na rodada cega).</summary>
        public void ShowUnknown()
        {
            ShowBack();
            question.gameObject.SetActive(true);
        }

        public void SetHighlight(bool on, Color? color = null)
        {
            outline.gameObject.SetActive(on);
            if (on) outline.color = color ?? Palette.Accent;
        }

        public void SetDimmed(bool dimmed)
        {
            group.alpha = dimmed ? 0.45f : 1f;
        }

        public void SetInteractable(bool interactable)
        {
            group.blocksRaycasts = interactable;
        }

        public void SetShadow(bool on)
        {
            shadow.gameObject.SetActive(on);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Clicked?.Invoke(this);
        }
    }
}
