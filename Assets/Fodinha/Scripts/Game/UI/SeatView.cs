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
        /// <summary>Tamanho total: avatar (fora do quadrado) + quadrado com nome, vidas e carta.</summary>
        public static readonly Vector2 BaseSize = new Vector2(300f, 150f);
        public const float AvatarSize = 96f;
        public const float AvatarGap = 6f;
        public static readonly Vector2 MiniCardSize = new Vector2(70f, 100f);

        private CanvasGroup group;
        private Image glow;
        private Image panel;
        private Image outline;
        private Text nameText;
        private AvatarView avatar;
        private Image heartIcon;
        private Text livesText;
        private Text betText;
        private CardView foreheadCard;   // rodada cega: a carta do adversário, "na testa" (em cima do avatar)
        private Image betBadge;          // aposta em destaque (amarelo, número grande)
        private Text betNumber;
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

            // Avatar à esquerda, FORA do quadrado; o quadrado fica só com nome, vidas e a carta (maior).
            avatar = AvatarView.Create(rt, "Avatar");
            avatar.Image.rectTransform.Place(new Vector2(AvatarSize * 0.5f / BaseSize.x, 0.5f), new Vector2(AvatarSize, AvatarSize));

            var box = UiKit.Rect("Caixa", rt);
            box.Anchor((AvatarSize + AvatarGap) / BaseSize.x, 0f, 1f, 1f);

            glow = UiKit.Image("Brilho", box, new Color(Palette.Accent.r, Palette.Accent.g, Palette.Accent.b, 0.55f), SpriteFactory.SoftGlow);
            glow.rectTransform.Fill();
            glow.rectTransform.offsetMin = new Vector2(-50f, -50f);
            glow.rectTransform.offsetMax = new Vector2(50f, 50f);
            glow.gameObject.AddComponent<Pulse>();

            panel = UiKit.Image("Painel", box, Palette.Panel, SpriteFactory.RoundedRect, 1.6f);
            panel.rectTransform.Fill();

            outline = UiKit.Image("Contorno", box, Palette.Accent, SpriteFactory.RoundedOutline, 1.6f);
            outline.rectTransform.Fill();

            nameText = UiKit.Label("Nome", box, "", 34, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Bold, true);
            nameText.rectTransform.Anchor(0.06f, 0.68f, 0.6f, 0.97f);

            // Vidas: coração desenhado + número (o símbolo ♥ da fonte não aparece no navegador).
            heartIcon = UiKit.Image("Coracao", box, Palette.Heart, SpriteFactory.Suit(Suit.Hearts));
            heartIcon.preserveAspect = true;
            heartIcon.raycastTarget = false;
            heartIcon.rectTransform.Anchor(0.06f, 0.42f, 0.2f, 0.66f);
            livesText = UiKit.Label("Vidas", box, "", 34, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Bold, true);
            livesText.rectTransform.Anchor(0.22f, 0.40f, 0.58f, 0.68f);

            betText = UiKit.Label("Fez", box, "", 30, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Normal, true);
            betText.rectTransform.Anchor(0.06f, 0.04f, 0.6f, 0.38f);

            // APOSTA em destaque: plaquinha amarela com o número bem grande, à direita do quadrado.
            betBadge = UiKit.Image("Aposta", box, Palette.Accent, SpriteFactory.RoundedRect, 1.6f);
            betBadge.raycastTarget = false;
            betBadge.rectTransform.Anchor(0.61f, 0.06f, 0.97f, 0.94f);
            betCaption = UiKit.Label("Legenda", betBadge.transform, "APOSTA", 22, Palette.AccentDark, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            betCaption.rectTransform.Anchor(0.04f, 0.74f, 0.96f, 0.97f);
            betNumber = UiKit.Label("Numero", betBadge.transform, "?", 84, Palette.AccentDark, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            betNumber.rectTransform.Anchor(0f, 0.0f, 1f, 0.8f);

            // Rodada cega: a carta do adversário aparece "na testa", em cima do avatar.
            foreheadCard = CardView.Create(rt, "CartaNaTesta");
            foreheadCard.Rect.Place(new Vector2(AvatarSize * 0.5f / BaseSize.x, 0.56f), MiniCardSize);
            foreheadCard.SetInteractable(false);
            foreheadCard.gameObject.SetActive(false);

            // Distribuidor: ficha no canto de baixo do avatar.
            dealerChip = UiKit.Image("Distribuidor", rt, Palette.Accent, SpriteFactory.Circle);
            dealerChip.rectTransform.Place(new Vector2((AvatarSize - 12f) / BaseSize.x, 0.24f), new Vector2(42f, 42f));
            var d = UiKit.Label("D", dealerChip.transform, "D", 30, Palette.AccentDark, TextAnchor.MiddleCenter, FontStyle.Bold);
            d.rectTransform.Fill();

            timer = TimerBar.Create(box);
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
                foreheadCard.gameObject.SetActive(false);
                betBadge.gameObject.SetActive(false);
                return;
            }

            livesText.text = p.Lives.ToString();
            betText.text = WonLine(p);
            betText.color = Palette.Text;
            betBadge.gameObject.SetActive(true);
            betNumber.text = p.HasBet ? p.Bet.ToString() : "?";
            betBadge.color = p.HasBet ? Palette.Accent : new Color(Palette.Accent.r, Palette.Accent.g, Palette.Accent.b, 0.45f);

            // Animações: aposta feita "pula"; vaza ganha também.
            if (p.HasBet && lastBet != p.Bet) Tweens.Scale(betBadge.rectTransform, new Vector3(1.3f, 1.3f, 1f), Vector3.one, 0.35f);
            else if (p.HasBet && p.TricksWon > lastTricks) Tweens.Scale(betText.rectTransform, new Vector3(1.25f, 1.25f, 1f), Vector3.one, 0.3f);
            if (p.Lives < lastLives && lastLives >= 0) Tweens.Scale(livesText.rectTransform, new Vector3(1.5f, 1.5f, 1f), Vector3.one, 0.45f);
            lastBet = p.Bet;
            lastTricks = p.TricksWon;
            lastLives = p.Lives;

            // Rodada cega: o local enxerga a carta "na testa" deste adversário.
            bool blind = p.VisibleCards.Count > 0;
            foreheadCard.gameObject.SetActive(blind);
            if (blind)
            {
                var card = p.VisibleCards[0];
                foreheadCard.ShowFace(card, view.Comparer != null && view.Comparer.IsManilha(card));
            }
        }

        /// <summary>Aposta mostrada na plaquinha ("?" enquanto não apostou).</summary>
        public string ShownBet => betBadge.gameObject.activeSelf ? betNumber.text : "";

        /// <summary>Carta da testa à mostra (só na rodada cega).</summary>
        public bool ShowsForeheadCard => foreheadCard.gameObject.activeSelf;

        /// <summary>Avatar do jogador deste assento (vazio = sorteado pelo nome).</summary>
        public void SetAvatar(string encoded, string seed) => avatar.Show(encoded, seed);

        public string ShownAvatar => avatar.Current;

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
