using System;
using System.Collections.Generic;
using Fodinha.Core;
using Fodinha.Game.UI;
using Fodinha.Hosting;
using Fodinha.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Fodinha.Online
{
    /// <summary>
    /// Sala de espera: código para compartilhar, jogadores, regras e (só para o dono) regras, bots e iniciar.
    /// Só desenha o <see cref="RoomStateDto"/> e dispara pedidos; quem decide é o servidor.
    /// </summary>
    public sealed class LobbyScreen : MonoBehaviour
    {
        private bool built;
        private RectTransform safeRoot;
        private Text codeText;
        private Text visibilityText;
        private Text playersTitle;
        private RectTransform playersList;
        private Text rulesText;
        private Button rulesButton;
        private Button botButton;
        private Button startButton;
        private Text waitingText;
        private Image banner;
        private Text bannerText;
        private Image toastBg;
        private Text toastText;
        private float toastUntil;
        private GameObject rulesOverlay;
        private RulesEditor rulesEditor;
        private RoomStateDto room;

        public event Action LeaveRequested;
        public event Action AddBotRequested;
        public event Action<int> RemoveBotRequested;
        public event Action StartRequested;
        public event Action<RoomOptions> OptionsSubmitted;

        public RoomStateDto Room => room;
        public Button StartButton => startButton;
        public Button BotButton => botButton;
        public Button RulesButton => rulesButton;
        public int ListedPlayers { get; private set; }
        public string LastToast { get; private set; } = "";

        private void Awake() => EnsureBuilt();

        private void Update()
        {
            if (toastBg != null && toastBg.gameObject.activeSelf && Time.unscaledTime > toastUntil) toastBg.gameObject.SetActive(false);
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (rulesOverlay != null && rulesOverlay.activeSelf) rulesOverlay.SetActive(false);
                else LeaveRequested?.Invoke();
            }
        }

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

            // Cabeçalho
            var header = UiKit.Rect("Cabecalho", safeRoot).Anchor(0f, 0.925f, 1f, 1f);
            var leave = UiKit.Button("Sair", header, "‹ Sair", () => LeaveRequested?.Invoke(), Palette.ButtonSecondary, Palette.Text, 34);
            leave.GetComponent<RectTransform>().Anchor(0.03f, 0.18f, 0.25f, 0.82f);
            var title = UiKit.Label("Titulo", header, "Sala", 54, Palette.Accent, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            title.rectTransform.Anchor(0.26f, 0.1f, 0.74f, 0.9f);

            // Código
            var card = UiKit.Image("Codigo", safeRoot, Palette.Panel, SpriteFactory.RoundedRect, 1f);
            card.rectTransform.Anchor(0.05f, 0.745f, 0.95f, 0.91f);
            var codeLabel = UiKit.Label("Rotulo", card.transform, "Código da sala", 32, Palette.TextMuted, TextAnchor.MiddleLeft, FontStyle.Normal, true);
            codeLabel.rectTransform.Anchor(0.05f, 0.7f, 0.6f, 0.95f);
            codeText = UiKit.Label("Texto", card.transform, "", 110, Palette.Accent, TextAnchor.MiddleLeft, FontStyle.Bold, true);
            codeText.rectTransform.Anchor(0.05f, 0.24f, 0.68f, 0.74f);
            var copy = UiKit.Button("Copiar", card.transform, "Copiar", CopyCode, Palette.Button, Palette.ButtonText, 38);
            copy.GetComponent<RectTransform>().Anchor(0.7f, 0.32f, 0.95f, 0.72f);
            visibilityText = UiKit.Label("Visibilidade", card.transform, "", 30, Palette.TextMuted, TextAnchor.MiddleLeft, FontStyle.Normal, true);
            visibilityText.rectTransform.Anchor(0.05f, 0.04f, 0.95f, 0.26f);

            // Jogadores
            playersTitle = UiKit.Label("TituloJogadores", safeRoot, "", 40, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Bold, true);
            playersTitle.rectTransform.Anchor(0.06f, 0.69f, 0.94f, 0.735f);
            playersList = UiControls.ScrollArea("Jogadores", safeRoot, 10f);
            ((RectTransform)playersList.parent).Anchor(0.05f, 0.335f, 0.95f, 0.69f);

            // Regras
            var rulesBox = UiKit.Image("Regras", safeRoot, Palette.Panel, SpriteFactory.RoundedRect, 1.5f);
            rulesBox.rectTransform.Anchor(0.05f, 0.225f, 0.95f, 0.325f);
            rulesText = UiKit.Label("Texto", rulesBox.transform, "", 30, Palette.TextMuted, TextAnchor.MiddleLeft, FontStyle.Normal, true);
            rulesText.rectTransform.Anchor(0.04f, 0.06f, 0.96f, 0.94f);

            // Ações do dono
            rulesButton = UiKit.Button("BotaoRegras", safeRoot, "Regras", OpenRules, Palette.ButtonSecondary, Palette.Text, 40);
            rulesButton.GetComponent<RectTransform>().Anchor(0.05f, 0.13f, 0.48f, 0.21f);
            botButton = UiKit.Button("BotaoBot", safeRoot, "+ Bot", () => AddBotRequested?.Invoke(), Palette.ButtonSecondary, Palette.Text, 40);
            botButton.GetComponent<RectTransform>().Anchor(0.52f, 0.13f, 0.95f, 0.21f);
            startButton = UiKit.Button("Iniciar", safeRoot, "Iniciar partida", () => StartRequested?.Invoke(), Palette.Button, Palette.ButtonText, 50);
            startButton.GetComponent<RectTransform>().Anchor(0.05f, 0.025f, 0.95f, 0.11f);
            waitingText = UiKit.Label("Aguardando", safeRoot, "Aguardando o dono da sala iniciar a partida...", 38, Palette.TextMuted,
                                      TextAnchor.MiddleCenter, FontStyle.Italic, true);
            waitingText.rectTransform.Anchor(0.05f, 0.04f, 0.95f, 0.2f);

            // Faixa de conexão e aviso
            banner = UiKit.Image("Conexao", safeRoot, new Color(0.7f, 0.35f, 0f, 0.95f), SpriteFactory.RoundedRect, 1.5f);
            banner.rectTransform.Anchor(0.05f, 0.87f, 0.95f, 0.92f);
            bannerText = UiKit.Label("Texto", banner.transform, "Conexão perdida. Reconectando...", 32, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            bannerText.rectTransform.Fill();
            banner.gameObject.SetActive(false);

            toastBg = UiKit.Image("Aviso", safeRoot, new Color(0.08f, 0.08f, 0.08f, 0.94f), SpriteFactory.RoundedRect, 1.2f);
            toastBg.rectTransform.Anchor(0.06f, 0.4f, 0.94f, 0.47f);
            toastBg.raycastTarget = false;
            toastText = UiKit.Label("Texto", toastBg.transform, "", 36, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            toastText.rectTransform.Fill();
            toastBg.gameObject.SetActive(false);

            // Página de regras (dono)
            var overlay = UiKit.Image("PaginaRegras", safeRoot, Palette.PanelStrong);
            overlay.rectTransform.Fill();
            rulesOverlay = overlay.gameObject;
            var otitle = UiKit.Label("Titulo", overlay.transform, "Regras da sala", 52, Palette.Accent, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            otitle.rectTransform.Anchor(0.05f, 0.93f, 0.95f, 0.99f);
            rulesEditor = RulesEditor.Create(overlay.transform, RulesEditor.Mode.Room);
            ((RectTransform)rulesEditor.transform).Anchor(0.04f, 0.13f, 0.96f, 0.92f);
            var cancel = UiKit.Button("Cancelar", overlay.transform, "Cancelar", () => rulesOverlay.SetActive(false), Palette.ButtonSecondary, Palette.Text, 44);
            cancel.GetComponent<RectTransform>().Anchor(0.05f, 0.025f, 0.38f, 0.11f);
            var save = UiKit.Button("Salvar", overlay.transform, "Salvar regras", SubmitRules, Palette.Button, Palette.ButtonText, 46);
            save.GetComponent<RectTransform>().Anchor(0.42f, 0.025f, 0.95f, 0.11f);
            rulesOverlay.SetActive(false);
        }

        // ------------------------------------------------------------------ desenho

        public void Render(RoomStateDto state)
        {
            EnsureBuilt();
            room = state;
            if (state == null) return;

            bool owner = state.IAmOwner;
            codeText.text = state.code;
            visibilityText.text = state.isPublic
                ? "Sala pública: aparece na lista e qualquer um pode entrar"
                : "Sala privada: só entra quem tiver o código";
            playersTitle.text = "Jogadores  " + UiKit.Colored(state.members.Count + "/" + state.maxPlayers, Palette.TextMuted);
            rulesText.text = RulesSummary(state.rules);

            UiKit.DestroyChildren(playersList);
            ListedPlayers = state.members.Count;
            for (int i = 0; i < state.members.Count; i++) AddPlayerRow(state.members[i], i, owner, state.you);

            rulesButton.gameObject.SetActive(owner);
            botButton.gameObject.SetActive(owner);
            startButton.gameObject.SetActive(owner);
            waitingText.gameObject.SetActive(!owner);
            botButton.interactable = state.members.Count < state.maxPlayers;
            startButton.interactable = state.members.Count >= RuleSet.MinPlayers;
            UiKit.ButtonLabel(startButton).text = state.members.Count >= RuleSet.MinPlayers
                ? "Iniciar partida (" + state.members.Count + " jogadores)"
                : "Precisa de 2 jogadores (adicione um bot)";
        }

        private void AddPlayerRow(MemberDto m, int index, bool iAmOwner, string you)
        {
            var row = UiKit.Image("Jogador_" + index, playersList, Palette.Panel, SpriteFactory.RoundedRect, 1.5f);
            UiControls.SetHeight(row, 96f);

            string name = m.name + (m.id == you ? UiKit.Colored("  (você)", Palette.Accent) : "");
            var nameText = UiKit.Label("Nome", row.transform, name, 40, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Bold, true);
            nameText.rectTransform.Anchor(0.04f, 0.1f, 0.62f, 0.9f);

            string tag = m.owner ? UiKit.Colored("dono", Palette.Accent)
                       : m.bot ? UiKit.Colored("bot", Palette.TextMuted)
                       : !m.connected ? UiKit.Colored("caiu", Palette.Bad)
                       : UiKit.Colored("pronto", Palette.Good);
            if (m.owner && !m.connected) tag += "  " + UiKit.Colored("caiu", Palette.Bad);
            var tagText = UiKit.Label("Etiqueta", row.transform, tag, 32, Palette.Text, TextAnchor.MiddleRight, FontStyle.Normal, true);
            tagText.rectTransform.Anchor(0.6f, 0.1f, iAmOwner && m.bot ? 0.8f : 0.96f, 0.9f);

            if (iAmOwner && m.bot)
            {
                var remove = UiKit.Button("Remover", row.transform, "X", () => RemoveBotRequested?.Invoke(index), Palette.Bad, Color.white, 40);
                remove.GetComponent<RectTransform>().Anchor(0.83f, 0.15f, 0.96f, 0.85f);
            }
        }

        public void SetConnected(bool connected)
        {
            EnsureBuilt();
            banner.gameObject.SetActive(!connected);
        }

        public void ShowToast(string message, float seconds = 2.6f)
        {
            EnsureBuilt();
            LastToast = message;
            toastText.text = message;
            toastBg.gameObject.SetActive(true);
            toastBg.transform.SetAsLastSibling();
            toastUntil = Time.unscaledTime + seconds;
        }

        // ------------------------------------------------------------------ ações

        private void CopyCode()
        {
            if (room == null) return;
            GUIUtility.systemCopyBuffer = room.code;
            ShowToast("Código " + room.code + " copiado! Mande para os amigos.");
        }

        public void OpenRules()
        {
            if (room == null) return;
            rulesEditor.Load(room.rules, room.maxPlayers, room.isPublic);
            rulesOverlay.SetActive(true);
            rulesOverlay.transform.SetAsLastSibling();
        }

        public bool IsRulesOpen => rulesOverlay != null && rulesOverlay.activeSelf;

        public void SubmitRules()
        {
            var options = new RoomOptions
            {
                IsPublic = rulesEditor.IsPublic,
                MaxPlayers = rulesEditor.PlayerOrBotCount,
                Rules = rulesEditor.BuildRules()
            };
            var errors = options.GetValidationErrors();
            if (errors.Count > 0) { ShowToast(errors[0]); return; }
            if (room != null && options.MaxPlayers < room.members.Count)
            {
                ShowToast("O máximo não pode ser menor que os " + room.members.Count + " jogadores na sala.");
                return;
            }
            rulesOverlay.SetActive(false);
            OptionsSubmitted?.Invoke(options);
        }

        /// <summary>Resumo de uma linha das regras (para quem não é dono ver o que foi escolhido).</summary>
        public static string RulesSummary(RuleSet r)
        {
            if (r == null) return "";
            // Só o que é configurável muda de sala para sala; o resto são as regras oficiais.
            var parts = new List<string>
            {
                r.StartingLives + " vidas",
                r.BetTimeSeconds + " s para apostar",
                r.TurnTimeSeconds + " s para jogar a carta"
            };
            return string.Join(" · ", parts) +
                   "\nRegras oficiais: manilha pela vira · naipe desempata · pé não fecha a soma · errou perde 1 vida · 1ª mão cega";
        }
    }
}
