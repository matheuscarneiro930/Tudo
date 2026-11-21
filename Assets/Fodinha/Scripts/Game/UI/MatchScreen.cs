using System;
using Fodinha.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fodinha.Game.UI
{
    /// <summary>
    /// Tela da partida em retrato. Monta toda a interface por código e apenas:
    ///  - desenha o <see cref="MatchView"/> recebido da sessão;
    ///  - envia pedidos ("apostar X", "jogar carta Y", "voltar a jogar").
    /// Não conhece o <see cref="MatchEngine"/> nem decide nenhuma regra.
    /// </summary>
    public sealed class MatchScreen : MonoBehaviour
    {
        [Tooltip("Componente que implementa IMatchSession (ex.: LocalMatchController).")]
        [SerializeField] private MonoBehaviour sessionSource;

        private IMatchSession session;
        private bool built;

        private RectTransform safeRoot;
        private Text roundTitle;
        private Text roundDetail;

        private Text manilhaText;
        private Button exitButton;
        private TableView table;
        private Image localPanel;
        private Image localOutline;
        private Text localName;
        private Image localHeart;
        private Text localLives;
        private Text localBet;
        private Image localDealer;
        private TimerBar localTimer;
        private HandView hand;
        private BetPanel betPanel;
        private Text statusText;
        private Button resumeButton;
        private Image toastBg;
        private Text toastText;
        private float toastUntil;
        private RoundResultPanel roundPanel;
        private MatchOverPanel matchOverPanel;
        private MatchView view;

        /// <summary>Jogador pediu outra partida (tela de fim).</summary>
        public event Action PlayAgainRequested;

        /// <summary>Jogador pediu para voltar ao menu.</summary>
        public event Action MenuRequested;

        public IMatchSession Session => session;
        public HandView Hand => hand;
        public BetPanel BetPanel => betPanel;
        public TableView Table => table;
        public MatchView CurrentView => view;
        public bool IsRoundPanelVisible => roundPanel != null && roundPanel.gameObject.activeSelf;
        public bool IsMatchOverVisible => matchOverPanel != null && matchOverPanel.gameObject.activeSelf;
        public bool IsBetPanelVisible => betPanel != null && betPanel.gameObject.activeSelf;

        private void Awake()
        {
            EnsureBuilt();
            if (session == null && sessionSource is IMatchSession s) Bind(s);
        }

        private void OnDestroy()
        {
            Unbind();
        }

        /// <summary>Liga a tela a uma sessão (offline ou online).</summary>
        public void Bind(IMatchSession newSession)
        {
            EnsureBuilt();
            if (ReferenceEquals(session, newSession)) return;
            Unbind();
            session = newSession;
            if (session == null) return;
            session.ViewChanged += Render;
            session.ActionRejected += OnActionRejected;
            session.EventReceived += OnMatchEvent;
            if (session.View != null) Render(session.View);
        }

        private void Unbind()
        {
            if (session == null) return;
            session.ViewChanged -= Render;
            session.ActionRejected -= OnActionRejected;
            session.EventReceived -= OnMatchEvent;
            session = null;
        }

        /// <summary>Sons e vibração a partir dos eventos públicos da partida.</summary>
        private void OnMatchEvent(MatchEvent e)
        {
            int me = session != null ? session.LocalSeat : -1;
            switch (e.Type)
            {
                case MatchEventType.CardsDealt:
                    if (Application.isPlaying && isActiveAndEnabled) StartCoroutine(DealSounds(Mathf.Clamp(e.Value * 2, 2, 8)));
                    break;
                case MatchEventType.CardPlayed:
                    Audio.Sfx.Play(Audio.Sound.CardOnTable);
                    break;
                case MatchEventType.TurnChanged:
                    if (e.Seat == me && me >= 0 && (session.View == null || !session.View.Players[me].IsAutoPlay))
                    {
                        Audio.Sfx.Play(Audio.Sound.YourTurn);
                        Audio.Sfx.Vibrate();
                    }
                    break;
                case MatchEventType.TrickResolved:
                    if (e.Seat == me && me >= 0) Audio.Sfx.Play(Audio.Sound.TrickWon);
                    break;
                case MatchEventType.LifeLost:
                    if (e.Seat == me && me >= 0) Audio.Sfx.Play(Audio.Sound.LifeLost);
                    break;
                case MatchEventType.MatchEnded:
                    if (me >= 0) Audio.Sfx.Play(e.Seat == me ? Audio.Sound.Victory : Audio.Sound.Defeat);
                    break;
            }
        }

        private bool matchOverShown;

        /// <summary>Segundos mostrando o resultado antes do anúncio de fim de partida.</summary>
        public const float EndOfMatchAdDelay = 2.5f;

        /// <summary>Fim de partida: anúncio de tela cheia (no celular), depois de mostrar o resultado.</summary>
        private void ShowEndOfMatchAd()
        {
            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                Ads.AdService.ShowInterstitial();
                return;
            }
            StartCoroutine(EndOfMatchAd());
        }

        private System.Collections.IEnumerator EndOfMatchAd()
        {
            yield return new WaitForSecondsRealtime(EndOfMatchAdDelay);
            Ads.AdService.ShowInterstitial();
        }

        private static System.Collections.IEnumerator DealSounds(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Audio.Sfx.Play(Audio.Sound.Card, 0.8f);
                yield return new WaitForSecondsRealtime(0.06f);
            }
        }

        /// <summary>Texto do botão principal da tela de fim (offline: "Jogar de novo"; online: "Voltar à sala").</summary>
        public void SetPlayAgainLabel(string label)
        {
            EnsureBuilt();
            matchOverPanel.SetPlayAgainLabel(label);
        }

        // ------------------------------------------------------------------ montagem

        private void EnsureBuilt()
        {
            if (built) return;
            built = true;

            var root = (RectTransform)transform;
            var bg = UiKit.Image("Fundo", root, Color.white, SpriteFactory.Background);
            bg.rectTransform.Fill();
            bg.raycastTarget = false;

            safeRoot = UiKit.Rect("AreaSegura", root).Fill();
            safeRoot.gameObject.AddComponent<SafeAreaFitter>();

            BuildTopBar(UiKit.Rect("Topo", safeRoot).Anchor(0f, 0.945f, 1f, 1f));

            table = TableView.Create(safeRoot);
            ((RectTransform)table.transform).Anchor(0f, 0.44f, 1f, 0.945f);

            BuildLocalStrip(UiKit.Rect("Jogador", safeRoot).Anchor(0.03f, 0.39f, 0.97f, 0.44f));

            hand = HandView.Create(safeRoot);
            ((RectTransform)hand.transform).Anchor(0f, 0.165f, 1f, 0.39f);
            hand.PlayRequested += card => session?.PlayCard(card);

            var action = UiKit.Rect("Acoes", safeRoot).Anchor(0.03f, 0.012f, 0.97f, 0.165f);
            betPanel = BetPanel.Create(action);
            ((RectTransform)betPanel.transform).Fill();
            betPanel.BetChosen += bet => session?.PlaceBet(bet);

            statusText = UiKit.Label("Status", action, "", 40, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            statusText.rectTransform.Anchor(0f, 0.45f, 1f, 1f);

            resumeButton = UiKit.Button("VoltarAJogar", action, "Voltar a jogar", () => session?.ResumeControl(), Palette.Button, Palette.ButtonText, 44);
            resumeButton.GetComponent<RectTransform>().Anchor(0.2f, 0.05f, 0.8f, 0.42f);

            toastBg = UiKit.Image("Aviso", safeRoot, new Color(0.6f, 0.1f, 0.1f, 0.92f), SpriteFactory.RoundedRect, 1.2f);
            toastBg.rectTransform.Anchor(0.08f, 0.40f, 0.92f, 0.46f);
            toastBg.raycastTarget = false;
            toastText = UiKit.Label("Texto", toastBg.transform, "", 36, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            toastText.rectTransform.Fill();
            toastBg.gameObject.SetActive(false);

            roundPanel = RoundResultPanel.Create(safeRoot);
            roundPanel.Hide();

            matchOverPanel = MatchOverPanel.Create(safeRoot);
            matchOverPanel.PlayAgainClicked += () => PlayAgainRequested?.Invoke();
            matchOverPanel.MenuClicked += () => MenuRequested?.Invoke();
            matchOverPanel.Hide();

            betPanel.gameObject.SetActive(false);
            resumeButton.gameObject.SetActive(false);
            statusText.text = "Preparando a mesa...";
        }

        private void BuildTopBar(RectTransform bar)
        {
            var bg = UiKit.Image("FundoTopo", bar, new Color(0f, 0f, 0f, 0.35f));
            bg.rectTransform.Fill();
            bg.raycastTarget = false;

            exitButton = UiKit.Button("Sair", bar, "‹ Sair", () => MenuRequested?.Invoke(), Palette.ButtonSecondary, Palette.Text, 32);
            exitButton.GetComponent<RectTransform>().Anchor(0.02f, 0.18f, 0.17f, 0.82f);

            // Topo: só quantas cartas tem a mão ("3 cartas"); a vira fica no meio da mesa.
            roundTitle = UiKit.Label("Cartas", bar, "", 44, Palette.Text, TextAnchor.LowerLeft, FontStyle.Bold, true);
            roundTitle.rectTransform.Anchor(0.2f, 0.45f, 0.66f, 0.95f);
            roundDetail = UiKit.Label("Detalhe", bar, "", 30, Palette.TextMuted, TextAnchor.UpperLeft, FontStyle.Normal, true);
            roundDetail.rectTransform.Anchor(0.2f, 0.05f, 0.66f, 0.45f);

            manilhaText = UiKit.Label("Manilha", bar, "", 34, Palette.Accent, TextAnchor.MiddleRight, FontStyle.Bold, true);
            manilhaText.rectTransform.Anchor(0.64f, 0.1f, 0.97f, 0.9f);
        }

        private void BuildLocalStrip(RectTransform strip)
        {
            localPanel = UiKit.Image("Painel", strip, Palette.Panel, SpriteFactory.RoundedRect, 1.5f);
            localPanel.rectTransform.Fill();
            localPanel.raycastTarget = false;
            localOutline = UiKit.Image("Contorno", strip, Palette.Accent, SpriteFactory.RoundedOutline, 1.5f);
            localOutline.rectTransform.Fill();
            localOutline.raycastTarget = false;

            localDealer = UiKit.Image("Distribuidor", strip, Palette.Accent, SpriteFactory.Circle);
            localDealer.rectTransform.Place(new Vector2(0.035f, 0.55f), new Vector2(54f, 54f));
            var d = UiKit.Label("D", localDealer.transform, "D", 34, Palette.AccentDark, TextAnchor.MiddleCenter, FontStyle.Bold);
            d.rectTransform.Fill();

            localName = UiKit.Label("Nome", strip, "", 38, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Bold, true);
            localName.rectTransform.Anchor(0.08f, 0.2f, 0.38f, 0.95f);
            localHeart = UiKit.Image("Coracao", strip, Palette.Heart, SpriteFactory.Suit(Suit.Hearts));
            localHeart.preserveAspect = true;
            localHeart.raycastTarget = false;
            localHeart.rectTransform.Anchor(0.39f, 0.28f, 0.45f, 0.88f);
            localLives = UiKit.Label("Vidas", strip, "", 40, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Bold, true);
            localLives.rectTransform.Anchor(0.46f, 0.2f, 0.58f, 0.95f);
            localBet = UiKit.Label("Aposta", strip, "", 36, Palette.Text, TextAnchor.MiddleRight, FontStyle.Normal, true);
            localBet.rectTransform.Anchor(0.58f, 0.2f, 0.97f, 0.95f);

            localTimer = TimerBar.Create(strip);
            localTimer.GetComponent<RectTransform>().Anchor(0.02f, 0.04f, 0.98f, 0.16f);
        }

        // ------------------------------------------------------------------ desenho

        public void Render(MatchView newView)
        {
            EnsureBuilt();
            view = newView;
            if (view == null) return;

            RenderTopBar();
            table.Render(view, session != null ? session.TimerSeat : -1);
            RenderLocalStrip();
            hand.Render(view);
            RenderActions();

            if (view.Phase == MatchPhase.RoundComplete && view.LastRoundSummary != null)
                roundPanel.Show(view.LastRoundSummary, view);
            else
                roundPanel.Hide();

            if (view.Phase == MatchPhase.MatchOver)
            {
                matchOverPanel.Show(view, MenuRequested != null);
                if (!matchOverShown)
                {
                    matchOverShown = true;
                    ShowEndOfMatchAd();
                }
            }
            else
            {
                matchOverPanel.Hide();
                matchOverShown = false;
            }

            UpdateTimers();
        }

        private void RenderTopBar()
        {
            exitButton.gameObject.SetActive(MenuRequested != null);
            roundTitle.text = view.CardsThisRound > 0 ? CardsLabel(view.CardsThisRound) : "Fodinha";
            string detail = view.ActivePlayerCount + " na mesa";
            if (view.IsBlindRound) detail += "  ·  " + UiKit.Colored("CEGA", Palette.Accent);
            if (view.Phase == MatchPhase.Playing || view.Phase == MatchPhase.TrickComplete)
                detail += "  ·  rodada " + view.TrickNumber + " de " + view.CardsThisRound;
            roundDetail.text = detail;

            manilhaText.text = view.Vira.HasValue
                ? "Manilha: " + CardComparer.ManilhaFromVira(view.Vira.Value).Symbol()
                : "";
        }

        public static string CardsLabel(int cards) => cards + (cards == 1 ? " carta" : " cartas");

        private void RenderLocalStrip()
        {
            var me = view.Me;
            if (me == null)
            {
                localName.text = "Espectador";
                localLives.text = "";
                localBet.text = "";
                localDealer.gameObject.SetActive(false);
                localOutline.gameObject.SetActive(false);
                return;
            }

            localName.text = me.Name + (me.IsAutoPlay ? " " + UiKit.Colored("(auto)", Palette.TextMuted) : "");
            localHeart.gameObject.SetActive(!me.IsEliminated);
            localLives.text = me.IsEliminated ? UiKit.Colored("Eliminado", Palette.Bad) : me.Lives.ToString();
            localBet.text = me.IsEliminated ? "" : SeatView.BetLine(me);
            localDealer.gameObject.SetActive(view.DealerSeat == me.Seat && !me.IsEliminated);
            bool myTurn = view.IsMyTurn && (view.Phase == MatchPhase.Betting || view.Phase == MatchPhase.Playing);
            localOutline.gameObject.SetActive(myTurn);
        }

        private void RenderActions()
        {
            var me = view.Me;
            bool active = me != null && !me.IsEliminated;
            bool autoPlay = active && me.IsAutoPlay;
            bool myBet = active && !autoPlay && view.Phase == MatchPhase.Betting && view.IsMyTurn && view.AllowedBets.Count > 0;

            betPanel.gameObject.SetActive(myBet);
            if (myBet) betPanel.Show(view);

            resumeButton.gameObject.SetActive(autoPlay && view.Phase != MatchPhase.MatchOver);
            statusText.gameObject.SetActive(!myBet);
            statusText.rectTransform.Anchor(0f, autoPlay ? 0.45f : 0f, 1f, 1f);
            statusText.text = StatusMessage(me, active, autoPlay);
        }

        private string StatusMessage(PlayerView me, bool active, bool autoPlay)
        {
            if (view.Phase == MatchPhase.NotStarted) return "Preparando a mesa...";
            if (view.Phase == MatchPhase.MatchOver) return "";
            if (me != null && me.IsEliminated) return UiKit.Colored("Você foi eliminado. Assistindo à partida...", Palette.TextMuted);
            if (autoPlay) return "O bot está jogando por você";

            string current = view.CurrentSeat >= 0 ? view.Players[view.CurrentSeat].Name : "";
            switch (view.Phase)
            {
                case MatchPhase.Betting:
                    if (view.IsBlindRound)
                        return "Mão cega: você vê a carta dos outros, não a sua.\n" + UiKit.Colored(current + " está apostando...", Palette.TextMuted);
                    return current + " está apostando...";
                case MatchPhase.Playing:
                    if (view.IsMyTurn)
                        return UiKit.Colored("Sua vez!", Palette.Accent) + " Toque numa carta e toque de novo para jogar";
                    return "Vez de " + current;
                case MatchPhase.TrickComplete:
                    return "";
                case MatchPhase.RoundComplete:
                    return "Resultado";
            }
            return "";
        }

        private void Update()
        {
            if (view == null) return;
            UpdateTimers();
            if (toastBg.gameObject.activeSelf && Time.unscaledTime > toastUntil) toastBg.gameObject.SetActive(false);
        }

        private void UpdateTimers()
        {
            int timerSeat = session != null ? session.TimerSeat : -1;
            float total = session != null ? Mathf.Max(0.01f, session.TurnTimeTotal) : 1f;
            float fraction = session != null ? session.TurnTimeRemaining / total : 0f;

            table.UpdateTimer(timerSeat, fraction);
            bool mine = timerSeat >= 0 && timerSeat == view.ViewerSeat;
            localTimer.SetVisible(mine);
            if (mine) localTimer.SetFraction(fraction);

            if (roundPanel.gameObject.activeSelf && session != null) roundPanel.SetCountdown(session.PhaseWaitRemaining);
        }

        // ------------------------------------------------------------------ avisos

        private void OnActionRejected(ActionResult result)
        {
            Audio.Sfx.Play(Audio.Sound.Error);
            ShowToast(Describe(result));
        }

        public void ShowToast(string message, float seconds = 2.2f)
        {
            EnsureBuilt();
            toastText.text = message;
            toastBg.gameObject.SetActive(true);
            toastUntil = Time.unscaledTime + seconds;
        }

        public static string Describe(ActionResult result)
        {
            switch (result)
            {
                case ActionResult.NotYourTurn: return "Ainda não é a sua vez";
                case ActionResult.WrongPhase: return "Agora não dá para fazer isso";
                case ActionResult.BetOutOfRange: return "Aposta inválida";
                case ActionResult.BetForbiddenForLastBidder: return "O pé não pode fechar a soma das apostas";
                case ActionResult.CardNotInHand: return "Essa carta não está na sua mão";
                case ActionResult.PlayerEliminated: return "Você já foi eliminado";
                case ActionResult.InvalidSeat: return "Assento inválido";
                default: return "Ação recusada";
            }
        }
    }
}
