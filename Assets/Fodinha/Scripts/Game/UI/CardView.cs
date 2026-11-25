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
        private Image cornerSuit;
        private Image centerSuit;
        private Text bottomRank;
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
            cornerSuit = UiKit.Image("NaipeCanto", body.transform, Palette.BlackSuit, SpriteFactory.Suit(Suit.Spades));
            cornerSuit.preserveAspect = true;
            cornerSuit.raycastTarget = false;
            cornerSuit.rectTransform.Anchor(0.07f, 0.49f, 0.33f, 0.67f);
            centerSuit = UiKit.Image("NaipeCentro", body.transform, Palette.BlackSuit, SpriteFactory.Suit(Suit.Spades));
            centerSuit.preserveAspect = true;
            centerSuit.raycastTarget = false;
            centerSuit.rectTransform.Anchor(0.3f, 0.18f, 0.9f, 0.6f);
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

            outline = UiKit.Image("Contorno", rt, Palette.Accent, SpriteFactory.RoundedOutline, 2.5f);
            outline.rectTransform.Fill();
            outline.rectTransform.offsetMin = new Vector2(-5f, -5f);
            outline.rectTransform.offsetMax = new Vector2(5f, 5f);
            outline.raycastTarget = false;

            ShowBack();
        }

        /// <summary>
        /// Mostra a frente da carta. A manilha não tem marca nenhuma (nem etiqueta nem cor diferente):
        /// cada jogador descobre pela vira, então quem não presta atenção pode errar.
        /// </summary>
        public void ShowFace(Card card)
        {
            Card = card;
            bool red = card.Suit == Suit.Hearts || card.Suit == Suit.Diamonds;
            var color = red ? Palette.RedSuit : Palette.BlackSuit;
            string rank = card.Rank.Symbol();
            var suitSprite = SpriteFactory.Suit(card.Suit);

            body.gameObject.SetActive(true);
            backImage.gameObject.SetActive(false);
            cornerRank.text = rank;
            cornerSuit.sprite = suitSprite;
            centerSuit.sprite = suitSprite;
            bottomRank.text = rank;
            cornerRank.color = cornerSuit.color = centerSuit.color = bottomRank.color = color;
            body.color = Palette.CardFace;
        }

        public void ShowBack()
        {
            Card = null;
            body.gameObject.SetActive(false);
            backImage.gameObject.SetActive(true);
            question.gameObject.SetActive(false);
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
