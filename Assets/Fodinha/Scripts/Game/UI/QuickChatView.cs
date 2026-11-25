using System;
using System.Collections.Generic;
using Fodinha.Hosting;
using UnityEngine;
using UnityEngine.UI;

namespace Fodinha.Game.UI
{
    /// <summary>
    /// Mensagens prontas na mesa: botão de balão no topo, quadro com as mensagens (sem digitar nada) e o balão
    /// em cima de quem falou. Fica por cima de tudo (até do resultado da rodada), menos dos avisos de sair.
    /// Só desenha e pede: quem manda/recebe é a sessão (<see cref="IQuickChatSession"/>).
    /// </summary>
    public sealed class QuickChatView : MonoBehaviour
    {
        public const float BubbleMaxWidth = 600f;
        public const float BubbleHeight = 78f;
        private const float TailSize = 24f;
        private const float FadeSeconds = 0.35f;

        public static readonly Color FriendlyColor = Palette.Hex("2E7D4F");
        public static readonly Color TauntColor = Palette.Hex("B4561F");
        public static readonly Color ReactionColor = Palette.Hex("3D4F86");

        private sealed class Bubble
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public Text Text;
            public RectTransform Tail;
            public float Until;
        }

        private RectTransform root;
        private RectTransform bubbleLayer;
        private Button openButton;
        private Image openIcon;
        private GameObject panel;
        private Text muteLabel;
        private readonly List<Button> messageButtons = new List<Button>();
        private readonly Dictionary<int, Bubble> bubbles = new Dictionary<int, Bubble>();
        private readonly Vector3[] corners = new Vector3[4];

        private IQuickChatSession session;
        private Func<int, RectTransform> anchorOf;
        private Func<int> localSeat;
        private Action<string> toast;
        private QuickChatGate gate = new QuickChatGate();
        private bool buttonAllowed = true;

        public Button OpenButton => openButton;
        public IReadOnlyList<Button> MessageButtons => messageButtons;
        public Button MuteButton { get; private set; }
        public bool IsPanelOpen => panel != null && panel.activeSelf;
        public bool IsAvailable => session != null;

        public static QuickChatView Create(RectTransform parent)
        {
            var rt = UiKit.Rect("Mensagens", parent).Fill();
            var view = rt.gameObject.AddComponent<QuickChatView>();
            view.Build(rt);
            return view;
        }

        private void Build(RectTransform rt)
        {
            root = rt;
            bubbleLayer = UiKit.Rect("Baloes", rt).Fill();

            // Botão no canto direito do topo (fica livre mesmo com o resultado da rodada ou o fim da partida na tela).
            openButton = UiKit.Button("BotaoMensagens", rt, "", Toggle, Palette.ButtonSecondary, Palette.Text, 30);
            openButton.GetComponent<RectTransform>().Place(new Vector2(0.935f, 0.9725f), new Vector2(112f, 76f));
            openIcon = UiKit.Image("Icone", openButton.transform, Color.white, SpriteFactory.ChatIcon);
            openIcon.preserveAspect = true;
            openIcon.raycastTarget = false;
            openIcon.rectTransform.Anchor(0.18f, 0.1f, 0.82f, 0.9f);

            // Quadro com as mensagens: toque fora fecha.
            var blocker = UiKit.Image("QuadroMensagens", rt, new Color(0f, 0f, 0f, 0.45f));
            blocker.rectTransform.Fill();
            var close = blocker.gameObject.AddComponent<Button>();
            close.transition = Selectable.Transition.None;
            close.onClick.AddListener(Close);
            panel = blocker.gameObject;

            var box = UiKit.Image("Caixa", blocker.transform, Palette.PanelStrong, SpriteFactory.RoundedRect, 0.8f);
            box.rectTransform.Anchor(0.03f, 0.27f, 0.97f, 0.935f);
            box.gameObject.AddComponent<Button>().transition = Selectable.Transition.None; // toque na caixa não fecha
            var border = UiKit.Image("Borda", box.transform, new Color(1f, 0.78f, 0.24f, 0.6f), SpriteFactory.RoundedOutline, 0.8f);
            border.rectTransform.Fill();
            border.raycastTarget = false;

            var title = UiKit.Label("Titulo", box.transform, "Mensagens rápidas", 42, Palette.Accent, TextAnchor.MiddleLeft, FontStyle.Bold, true);
            title.rectTransform.Anchor(0.05f, 0.905f, 0.55f, 0.98f);

            MuteButton = UiKit.Button("VerDosOutros", box.transform, "", ToggleMute, Palette.ButtonSecondary, Palette.Text, 28);
            MuteButton.GetComponent<RectTransform>().Anchor(0.56f, 0.912f, 0.95f, 0.972f);
            muteLabel = UiKit.ButtonLabel(MuteButton);

            var grid = UiKit.Rect("Lista", box.transform).Anchor(0.04f, 0.025f, 0.96f, 0.895f);
            int rows = (QuickChat.Count + 1) / 2;
            for (int id = 0; id < QuickChat.Count; id++)
            {
                int captured = id;
                var b = UiKit.Button("Mensagem" + id, grid, QuickChat.Text(id), () => Send(captured), ColorOf(QuickChat.KindOf(id)), Color.white, 36);
                int row = id / 2, col = id % 2;
                float h = 1f / rows;
                b.GetComponent<RectTransform>().Anchor(col * 0.5f + 0.01f, 1f - h * (row + 1) + 0.006f, col * 0.5f + 0.49f, 1f - h * row - 0.006f);
                messageButtons.Add(b);
            }

            panel.SetActive(false);
            RefreshMuteLabel();
            SetButtonVisible(false);
        }

        public static Color ColorOf(QuickChatKind kind) =>
            kind == QuickChatKind.Friendly ? FriendlyColor : kind == QuickChatKind.Taunt ? TauntColor : ReactionColor;

        // ------------------------------------------------------------------ ligação com a sessão

        /// <summary>
        /// <paramref name="anchorForSeat"/>: onde fica cada assento na tela (o balão aparece em cima).
        /// <paramref name="showToast"/>: aviso curto ("espere um pouco").
        /// </summary>
        public void Bind(IQuickChatSession chatSession, Func<int, RectTransform> anchorForSeat, Func<int> mySeat, Action<string> showToast)
        {
            Unbind();
            session = chatSession;
            anchorOf = anchorForSeat;
            localSeat = mySeat;
            toast = showToast;
            gate = new QuickChatGate();
            if (session != null) session.ChatReceived += Show;
            SetButtonVisible(buttonAllowed);
        }

        public void Unbind()
        {
            if (session != null) session.ChatReceived -= Show;
            session = null;
            Close();
            foreach (var b in bubbles.Values) b.Rect.gameObject.SetActive(false);
            SetButtonVisible(false);
        }

        /// <summary>Mostra o botão só quando dá para falar (ex.: espectador não fala).</summary>
        public void SetButtonVisible(bool visible)
        {
            buttonAllowed = visible;
            bool show = visible && session != null;
            if (openButton.gameObject.activeSelf != show) openButton.gameObject.SetActive(show);
            if (!show) Close();
        }

        // ------------------------------------------------------------------ quadro

        public void Toggle()
        {
            if (IsPanelOpen) Close();
            else Open();
        }

        public void Open()
        {
            if (session == null) return;
            panel.SetActive(true);
            panel.transform.SetAsLastSibling();
        }

        public void Close()
        {
            if (panel != null) panel.SetActive(false);
        }

        private void ToggleMute()
        {
            GamePrefs.QuickChatOn = !GamePrefs.QuickChatOn;
            RefreshMuteLabel();
            if (!GamePrefs.QuickChatOn)
            {
                int me = localSeat != null ? localSeat() : -1;
                foreach (var kv in bubbles)
                    if (kv.Key != me) kv.Value.Rect.gameObject.SetActive(false);
            }
        }

        private void RefreshMuteLabel() =>
            muteLabel.text = GamePrefs.QuickChatOn ? "Ver dos outros: SIM" : "Ver dos outros: NÃO";

        /// <summary>Manda a mensagem (com o mesmo limite do servidor: avisa em vez de engolir em silêncio).</summary>
        public void Send(int messageId)
        {
            if (session == null || !QuickChat.IsValid(messageId)) return;
            Close();
            if (!gate.TryAccept(Time.unscaledTimeAsDouble))
            {
                toast?.Invoke("Calma! Espere um pouquinho para mandar outra.");
                return;
            }
            session.SendChat(messageId);
        }

        // ------------------------------------------------------------------ balões

        /// <summary>Balão em cima de quem falou. Dos outros, só se "Ver dos outros" estiver ligado.</summary>
        public void Show(int seat, int messageId)
        {
            if (!QuickChat.IsValid(messageId) || seat < 0) return;
            int me = localSeat != null ? localSeat() : -1;
            if (seat != me && !GamePrefs.QuickChatOn) return;

            if (!bubbles.TryGetValue(seat, out var b)) bubbles[seat] = b = NewBubble(seat);
            b.Text.text = QuickChat.Text(messageId);
            float width = Mathf.Min(BubbleMaxWidth, b.Text.preferredWidth + 52f);
            b.Rect.sizeDelta = new Vector2(width, BubbleHeight);
            b.Rect.gameObject.SetActive(true);
            b.Rect.SetAsLastSibling();
            b.Group.alpha = 1f;
            b.Until = Time.unscaledTime + QuickChat.BubbleSeconds;
            PlaceBubble(b, anchorOf?.Invoke(seat));
            Tweens.Scale(b.Rect, new Vector3(0.55f, 0.55f, 1f), Vector3.one, 0.18f);
            Audio.Sfx.Play(Audio.Sound.Chat, 0.7f);
        }

        /// <summary>Texto do balão visível de um assento (null = nenhum). Para testes.</summary>
        public string BubbleText(int seat) =>
            bubbles.TryGetValue(seat, out var b) && b.Rect.gameObject.activeSelf ? b.Text.text : null;

        /// <summary>Posição do balão (para testes): centro, em coordenadas da tela de mensagens.</summary>
        public Vector2 BubblePosition(int seat) =>
            bubbles.TryGetValue(seat, out var b) ? (Vector2)root.InverseTransformPoint(b.Rect.TransformPoint(b.Rect.rect.center)) : Vector2.zero;

        private Bubble NewBubble(int seat)
        {
            var body = UiKit.Image("Balao" + seat, bubbleLayer, Color.white, SpriteFactory.RoundedRect, 1.4f);
            body.raycastTarget = false;
            var group = body.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            // Biquinho: um quadradinho girado, metade escondida atrás do balão.
            var tail = UiKit.Image("Bico", body.transform, Color.white);
            tail.raycastTarget = false;
            tail.rectTransform.sizeDelta = new Vector2(TailSize, TailSize);
            tail.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            tail.transform.SetAsFirstSibling();

            var text = UiKit.Label("Texto", body.transform, "", 34, Palette.BlackSuit, TextAnchor.MiddleCenter, FontStyle.Bold);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.rectTransform.Fill();

            var rt = body.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            return new Bubble { Rect = rt, Group = group, Text = text, Tail = tail.rectTransform };
        }

        /// <summary>Em cima do alvo; se não couber, embaixo. Nunca sai da tela pelos lados.</summary>
        private void PlaceBubble(Bubble b, RectTransform target)
        {
            var area = root.rect;
            Vector2 size = b.Rect.sizeDelta;
            Vector2 top, bottom;
            if (target != null && target.gameObject.activeInHierarchy)
            {
                target.GetWorldCorners(corners);
                top = root.InverseTransformPoint((corners[1] + corners[2]) * 0.5f);
                bottom = root.InverseTransformPoint((corners[0] + corners[3]) * 0.5f);
            }
            else
            {
                top = bottom = new Vector2(0f, area.yMax * 0.35f);
            }

            bool above = top.y + TailSize + size.y <= area.yMax - 90f || area.height <= 0f; // 90: barra do topo
            float y = above ? top.y + TailSize * 0.6f + size.y * 0.5f : bottom.y - TailSize * 0.6f - size.y * 0.5f;
            float x = top.x;
            if (area.width > 0f)
            {
                float half = size.x * 0.5f + 12f;
                x = Mathf.Clamp(x, area.xMin + half, area.xMax - half);
            }
            b.Rect.anchoredPosition = new Vector2(x, y);

            // Biquinho apontando para quem falou (embaixo do balão, ou em cima se ele foi para baixo).
            float tailX = Mathf.Clamp(top.x - x, -size.x * 0.5f + 30f, size.x * 0.5f - 30f);
            b.Tail.anchorMin = b.Tail.anchorMax = new Vector2(0.5f, above ? 0f : 1f);
            b.Tail.anchoredPosition = new Vector2(tailX, 0f);
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            foreach (var b in bubbles.Values)
            {
                if (!b.Rect.gameObject.activeSelf) continue;
                float left = b.Until - now;
                if (left <= 0f) b.Rect.gameObject.SetActive(false);
                else b.Group.alpha = Mathf.Clamp01(left / FadeSeconds);
            }
            // Botão "apagado" enquanto não pode mandar outra.
            if (openButton.gameObject.activeSelf)
                openIcon.color = gate.Wait(Time.unscaledTimeAsDouble) > 0 ? new Color(1f, 1f, 1f, 0.4f) : Color.white;
        }
    }
}
