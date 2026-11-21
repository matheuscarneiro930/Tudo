using Fodinha.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fodinha.Game.UI
{
    /// <summary>
    /// Um adversário na mesa: nome, vidas, aposta/vazas feitas, cartas na mão (ou a carta da testa na rodada cega),
    /// distribuidor e destaque de vez com relógio.
    /// </summary>
    public sealed class SeatView : MonoBehaviour
    {
        public static readonly Vector2 BaseSize = new Vector2(210f, 150f);

        private CanvasGroup group;
        private Image glow;
        private Image panel;
        private Image outline;
        private Text nameText;
        private Image heartIcon;
        private Text livesText;
        private Text betText;
        private CardView miniCard;
        private Text countText;
        private Text betCaption;
        private Image dealerChip;
        private TimerBar timer;

        public RectTransform Rect { get; private set; }
        public int Seat { get; private set; } = -1;

        private int lastBet = PlayerState.NoBet;
        private int lastTricks;
        private int lastLives = -1;

        public static SeatView Create(Transform parent)
        {
            var rt = UiKit.Rect("Assento", parent);
            rt.sizeDelta = BaseSize;
            var view = rt.gameObject.AddComponent<SeatView>();
            view.Build(rt);
            return view;
        }

        private void Build(RectTransform rt)
        {
            Rect = rt;
            group = gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;

            glow = UiKit.Image("Brilho", rt, new Color(Palette.Accent.r, Palette.Accent.g, Palette.Accent.b, 0.55f), SpriteFactory.SoftGlow);
            glow.rectTransform.Fill();
            glow.rectTransform.offsetMin = new Vector2(-50f, -50f);
            glow.rectTransform.offsetMax = new Vector2(50f, 50f);
            glow.gameObject.AddComponent<Pulse>();

            panel = UiKit.Image("Painel", rt, Palette.Panel, SpriteFactory.RoundedRect, 1.6f);
            panel.rectTransform.Fill();

            outline = UiKit.Image("Contorno", rt, Palette.Accent, SpriteFactory.RoundedOutline, 1.6f);
            outline.rectTransform.Fill();

            nameText = UiKit.Label("Nome", rt, "", 34, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Bold, true);
            nameText.rectTransform.Anchor(0.06f, 0.68f, 0.72f, 0.97f);

            // Vidas: coração desenhado + número (o símbolo ♥ da fonte não aparece no navegador).
            heartIcon = UiKit.Image("Coracao", rt, Palette.Heart, SpriteFactory.Suit(Suit.Hearts));
            heartIcon.preserveAspect = true;
            heartIcon.raycastTarget = false;
            heartIcon.rectTransform.Anchor(0.05f, 0.42f, 0.19f, 0.66f);
            livesText = UiKit.Label("Vidas", rt, "", 34, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Bold, true);
            livesText.rectTransform.Anchor(0.21f, 0.40f, 0.70f, 0.68f);

            betText = UiKit.Label("Fez", rt, "", 28, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Normal, true);
            betText.rectTransform.Anchor(0.05f, 0.04f, 0.70f, 0.38f);

            // Carta à direita: mostra a APOSTA do jogador (na rodada cega, a carta dele).
            miniCard = CardView.Create(rt, "Aposta");
            miniCard.Rect.Place(new Vector2(0.855f, 0.62f), new Vector2(50f, 72f));
            miniCard.SetShadow(false);
            miniCard.SetInteractable(false);

            betCaption = UiKit.Label("Legenda", rt, "aposta", 22, Palette.TextMuted, TextAnchor.MiddleCenter, FontStyle.Normal, true);
            betCaption.rectTransform.Anchor(0.70f, 0.02f, 1.0f, 0.24f);

            countText = UiKit.Label("Numero", miniCard.Rect, "", 40, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            countText.rectTransform.Fill();
            var countOutline = countText.gameObject.AddComponent<Outline>();
            countOutline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            countOutline.effectDistance = new Vector2(2f, -2f);

            dealerChip = UiKit.Image("Distribuidor", rt, Palette.Accent, SpriteFactory.Circle);
            dealerChip.rectTransform.Place(new Vector2(0f, 1f), new Vector2(46f, 46f), new Vector2(2f, -2f));
            var d = UiKit.Label("D", dealerChip.transform, "D", 30, Palette.AccentDark, TextAnchor.MiddleCenter, FontStyle.Bold);
            d.rectTransform.Fill();

            timer = TimerBar.Create(rt);
            timer.GetComponent<RectTransform>().Anchor(0.04f, -0.14f, 0.96f, -0.04f);
        }

        public void Render(PlayerView p, MatchView view, bool isTurn)
        {
            if (Seat != p.Seat)
            {
                // Widget reaproveitado para outro jogador: não anima a "mudança".
                lastBet = p.Bet;
                lastTricks = p.TricksWon;
                lastLives = p.Lives;
            }
            Seat = p.Seat;

            string suffix = "";
            if (!p.IsConnected) suffix = " " + UiKit.Colored("(caiu)", Palette.TextMuted);
            else if (p.IsAutoPlay) suffix = " " + UiKit.Colored("(auto)", Palette.TextMuted);
            nameText.text = p.Name + suffix;

            group.alpha = p.IsEliminated ? 0.4f : 1f;
            glow.gameObject.SetActive(isTurn && !p.IsEliminated);
            outline.gameObject.SetActive(isTurn && !p.IsEliminated);
            dealerChip.gameObject.SetActive(view.DealerSeat == p.Seat && !p.IsEliminated);

            if (p.IsEliminated)
            {
                livesText.text = "0";
                betText.text = "Eliminado";
                betText.color = Palette.TextMuted;
                miniCard.gameObject.SetActive(false);
                betCaption.gameObject.SetActive(false);
                return;
            }

            livesText.text = p.Lives.ToString();
            betText.text = WonLine(p);
            betText.color = Palette.Text;
            betCaption.gameObject.SetActive(true);

            // Animações: aposta feita "pula"; vaza ganha também.
            if (p.HasBet && lastBet != p.Bet) Tweens.Scale(betText.rectTransform, new Vector3(1.35f, 1.35f, 1f), Vector3.one, 0.35f);
            else if (p.HasBet && p.TricksWon > lastTricks) Tweens.Scale(betText.rectTransform, new Vector3(1.25f, 1.25f, 1f), Vector3.one, 0.3f);
            if (p.Lives < lastLives && lastLives >= 0) Tweens.Scale(livesText.rectTransform, new Vector3(1.5f, 1.5f, 1f), Vector3.one, 0.45f);
            lastBet = p.Bet;
            lastTricks = p.TricksWon;
            lastLives = p.Lives;

            string bet = p.HasBet ? p.Bet.ToString() : "?";
            miniCard.gameObject.SetActive(true);
            if (p.VisibleCards.Count > 0)
            {
                // Rodada cega: o local enxerga a carta "na testa" deste adversário; a aposta vai na legenda.
                var card = p.VisibleCards[0];
                miniCard.ShowFace(card, view.Comparer != null && view.Comparer.IsManilha(card));
                countText.text = "";
                betCaption.text = "aposta " + bet;
            }
            else
            {
                miniCard.ShowBack();
                countText.text = bet;
                betCaption.text = "aposta";
            }
        }

        /// <summary>Quantas rodadas o adversário já fez (verde se bateu a aposta, vermelho se passou).</summary>
        public static string WonLine(PlayerView p)
        {
            if (!p.HasBet) return UiKit.Colored("apostando...", Palette.TextMuted);
            string won = p.TricksWon == p.Bet ? UiKit.Colored(p.TricksWon.ToString(), Palette.Good)
                       : p.TricksWon > p.Bet ? UiKit.Colored(p.TricksWon.ToString(), Palette.Bad)
                       : p.TricksWon.ToString();
            return "Fez <b>" + won + "</b>";
        }

        public void SetTimer(bool visible, float fraction)
        {
            timer.SetVisible(visible);
            if (visible) timer.SetFraction(fraction);
        }

        /// <summary>Texto curto de aposta: "Aposta 2 · Fez 1" (verde quando bate a aposta).</summary>
        public static string BetLine(PlayerView p)
        {
            if (!p.HasBet) return UiKit.Colored("Aposta —", Palette.TextMuted);
            string won = p.TricksWon == p.Bet ? UiKit.Colored(p.TricksWon.ToString(), Palette.Good)
                       : p.TricksWon > p.Bet ? UiKit.Colored(p.TricksWon.ToString(), Palette.Bad)
                       : p.TricksWon.ToString();
            return "Aposta <b>" + p.Bet + "</b> · Fez <b>" + won + "</b>";
        }
    }
}
