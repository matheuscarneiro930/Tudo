using System;
using System.Collections.Generic;
using Fodinha.Core;
using Fodinha.Game.UI;
using Fodinha.Hosting;
using UnityEngine;
using UnityEngine.UI;

namespace Fodinha.Game.Menu
{
    /// <summary>
    /// Menu principal em retrato, montado por código. Só coleta escolhas e dispara pedidos
    /// (jogar offline, criar sala, entrar com código, atualizar salas públicas). Quem executa é o <see cref="MenuRoot"/>.
    /// </summary>
    public sealed partial class MenuScreen : MonoBehaviour
    {
        public enum Page { Home, Offline, CreateRoom, JoinRoom, PublicRooms, Settings, HowToPlay, Ranking, Avatar, Shop }

        private readonly Dictionary<Page, GameObject> pages = new Dictionary<Page, GameObject>();
        private bool built;
        private RectTransform safeRoot;
        private InputField homeName;
        private RulesEditor offlineRules;
        private RulesEditor roomRules;
        private InputField codeField;
        private Text codeHint;
        private Button joinButton;
        private RectTransform roomsList;
        private Text roomsEmpty;
        private InputField nameField;
        private InputField serverField;
        private OptionStepper soundStepper;
        private OptionStepper vibrationStepper;
        private Image toastBg;
        private Text toastText;
        private float toastUntil;
        private GameObject busyOverlay;
        private Text busyText;

        public event Action<OfflineMatchConfig> OfflineRequested;
        public event Action<RoomOptions> CreateRoomRequested;
        public event Action<string> JoinRoomRequested;
        public event Action RefreshRoomsRequested;
        public event Action RankingRequested;
        public event Action ClaimRewardRequested;

        /// <summary>Botão "Convidar amigos iOS": compartilhar o link do site.</summary>
        public event Action InviteIosRequested;

        public Page CurrentPage { get; private set; } = Page.Home;
        public RulesEditor OfflineRules => offlineRules;
        public RulesEditor RoomRules => roomRules;
        public InputField CodeField => codeField;
        public Button JoinButton => joinButton;
        public InputField NameField => nameField;
        public InputField HomeNameField => homeName;
        public string LastToast { get; private set; } = "";
        public int ListedRooms { get; private set; }

        private void Awake() => EnsureBuilt();

        private void Update()
        {
            if (toastBg != null && toastBg.gameObject.activeSelf && Time.unscaledTime > toastUntil) toastBg.gameObject.SetActive(false);
            // Botão "voltar" do Android.
            if (Input.GetKeyDown(KeyCode.Escape) && CurrentPage != Page.Home && (busyOverlay == null || !busyOverlay.activeSelf))
                ShowPage(CurrentPage == Page.HowToPlay ? Page.Settings : Page.Home);
        }

        // ------------------------------------------------------------------ navegação

        public void ShowPage(Page page)
        {
            EnsureBuilt();
            CurrentPage = page;
            foreach (var kv in pages) kv.Value.SetActive(kv.Key == page);

            switch (page)
            {
                case Page.Home:
                    homeName.text = GamePrefs.HasCustomName ? GamePrefs.PlayerName : "";
                    homeAvatar.Show(GamePrefs.Avatar);
                    break;
                case Page.Avatar:
                    LoadAvatarEditor();
                    break;
                case Page.Shop:
                    RefreshShop();
                    break;
                case Page.Offline:
                    offlineRules.Load(GamePrefs.OfflineRules, GamePrefs.OfflineBots);
                    break;
                case Page.CreateRoom:
                    var opts = GamePrefs.LastRoomOptions;
                    roomRules.Load(opts.Rules, opts.MaxPlayers, opts.IsPublic, opts.Stake);
                    break;
                case Page.JoinRoom:
                    codeField.text = "";
                    OnCodeChanged("");
                    break;
                case Page.Ranking:
                    RankingRequested?.Invoke();
                    break;
                case Page.PublicRooms:
                    RefreshRoomsRequested?.Invoke();
                    break;
                case Page.Settings:
                    nameField.text = GamePrefs.HasCustomName ? GamePrefs.PlayerName : "";
                    serverField.text = GamePrefs.HasCustomServer ? GamePrefs.ServerUrl : "";
                    soundStepper.SetIndex(GamePrefs.SoundOn ? 1 : 0);
                    vibrationStepper.SetIndex(GamePrefs.VibrationOn ? 1 : 0);
                    vipField.text = "";
                    RefreshVipStatus();
                    break;
            }
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
            var glow = UiKit.Image("Brilho", root, new Color(0.3f, 0.75f, 0.45f, 0.25f), SpriteFactory.SoftGlow);
            glow.rectTransform.Anchor(-0.4f, 0.45f, 1.4f, 1.05f);
            glow.raycastTarget = false;

            safeRoot = UiKit.Rect("AreaSegura", root).Fill();
            safeRoot.gameObject.AddComponent<SafeAreaFitter>();

            BuildHome();
            BuildOffline();
            BuildCreateRoom();
            BuildJoinRoom();
            BuildPublicRooms();
            BuildSettings();
            BuildHowToPlay();
            BuildRanking();
            BuildAvatarPage();
            BuildShop();

            toastBg = UiKit.Image("Aviso", safeRoot, new Color(0.08f, 0.08f, 0.08f, 0.94f), SpriteFactory.RoundedRect, 1.2f);
            toastBg.rectTransform.Anchor(0.06f, 0.13f, 0.94f, 0.2f);
            toastBg.raycastTarget = false;
            toastText = UiKit.Label("Texto", toastBg.transform, "", 36, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            toastText.rectTransform.Fill();
            toastText.rectTransform.offsetMin = new Vector2(20f, 6f);
            toastText.rectTransform.offsetMax = new Vector2(-20f, -6f);
            toastBg.gameObject.SetActive(false);

            var busy = UiKit.Image("Ocupado", safeRoot, new Color(0f, 0f, 0f, 0.75f));
            busy.rectTransform.Fill();
            busyOverlay = busy.gameObject;
            busyText = UiKit.Label("Texto", busy.transform, "", 46, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            busyText.rectTransform.Anchor(0.08f, 0.4f, 0.92f, 0.6f);
            busyOverlay.SetActive(false);

            ShowPage(Page.Home);
        }

        private RectTransform NewPage(Page page, string title)
        {
            var rt = UiKit.Rect("Pagina_" + page, safeRoot).Fill();
            pages[page] = rt.gameObject;
            if (title == null) return rt;

            var header = UiKit.Rect("Cabecalho", rt).Anchor(0f, 0.925f, 1f, 1f);
            var back = UiKit.Button("Voltar", header, "‹ Voltar", () => ShowPage(page == Page.HowToPlay ? Page.Settings : Page.Home),
                                    Palette.ButtonSecondary, Palette.Text, 34);
            back.GetComponent<RectTransform>().Anchor(0.03f, 0.18f, 0.25f, 0.82f);
            var t = UiKit.Label("Titulo", header, title, 54, Palette.Accent, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            t.rectTransform.Anchor(0.26f, 0.1f, 0.94f, 0.9f);
            return rt;
        }

        private static Button Footer(RectTransform page, string label, Action onClick)
        {
            var b = UiKit.Button("Acao", page, label, onClick, Palette.Button, Palette.ButtonText, 50);
            b.GetComponent<RectTransform>().Anchor(0.05f, 0.025f, 0.95f, 0.11f);
            return b;
        }

        private void BuildHome()
        {
            var page = NewPage(Page.Home, null);

            // Leque de cartas decorativo (as manilhas fixas + um 3).
            BuildWalletRow(page);

            var fan = UiKit.Rect("Leque", page).Anchor(0.1f, 0.71f, 0.9f, 0.94f);
            string[] cards = { "7O", "AE", "4P", "7C", "3P" };
            for (int i = 0; i < cards.Length; i++)
            {
                var cv = CardView.Create(fan, "Carta" + i);
                float angle = (i - 2) * -14f;
                cv.Rect.Place(new Vector2(0.5f, 0.38f), new Vector2(170f, 246f), new Vector2((i - 2) * 92f, -Mathf.Abs(i - 2) * 16f));
                cv.Rect.localRotation = Quaternion.Euler(0f, 0f, angle);
                cv.ShowFace(Card.Parse(cards[i]));
                cv.SetInteractable(false);
            }

            var title = UiKit.Label("Titulo", page, "FODINHA", 170, Palette.Accent, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            title.rectTransform.Anchor(0.04f, 0.6f, 0.96f, 0.7f);
            var shadow = title.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(4f, -6f);
            var sub = UiKit.Label("Sub", page, "O N L I N E", 48, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            sub.rectTransform.Anchor(0.1f, 0.565f, 0.9f, 0.605f);

            // Nome do jogador direto na tela inicial (aparece na mesa e nas salas).
            homeName = UiControls.TextField("Nome", page, "Digite seu nome", 40, GamePrefs.MaxNameLength);
            homeName.textComponent.alignment = TextAnchor.MiddleCenter;
            ((Text)homeName.placeholder).alignment = TextAnchor.MiddleCenter;
            homeName.onEndEdit.AddListener(value =>
            {
                GamePrefs.PlayerName = value;
                homeName.text = GamePrefs.HasCustomName ? GamePrefs.PlayerName : "";
            });
            homeName.GetComponent<RectTransform>().Anchor(0.27f, 0.503f, 0.9f, 0.557f);

            // Avatar ao lado do nome: toque para editar.
            var avatarButton = UiKit.Button("BotaoAvatar", page, "", () => ShowPage(Page.Avatar), new Color(0f, 0f, 0f, 0f), Palette.Text, 20);
            avatarButton.GetComponent<RectTransform>().Anchor(0.1f, 0.495f, 0.25f, 0.565f);
            homeAvatar = AvatarView.Create(avatarButton.transform, "Avatar");
            homeAvatar.Image.rectTransform.Fill();
            var edit = UiKit.Image("Editar", avatarButton.transform, Palette.Accent, SpriteFactory.Circle);
            edit.rectTransform.Place(new Vector2(0.86f, 0.14f), new Vector2(46f, 46f));
            edit.raycastTarget = false;
            var pencil = UiKit.Label("Lapis", edit.transform, "+", 40, Palette.AccentDark, TextAnchor.MiddleCenter, FontStyle.Bold);
            pencil.rectTransform.Fill();

            AddHomeButton(page, "Jogar offline", 0, true, () => ShowPage(Page.Offline));
            AddHomeButton(page, "Criar sala", 1, false, () => ShowPage(Page.CreateRoom));
            AddHomeButton(page, "Entrar com código", 2, false, () => ShowPage(Page.JoinRoom), 0);
            AddHomeButton(page, "Salas públicas", 2, false, () => ShowPage(Page.PublicRooms), 1);
            // Leva para o SITE do jogo (quem tem iPhone joga pelo navegador), não para uma sala.
            AddHomeButton(page, "Convidar amigos iOS", 3, false, () => InviteIosRequested?.Invoke());
            AddHomeButton(page, "Loja", 4, false, () => ShowPage(Page.Shop), 0);
            AddHomeButton(page, "Configurações", 4, false, () => ShowPage(Page.Settings), 1);

            var how = UiKit.Button("ComoJogar", page, "Como jogar?", () => ShowPage(Page.HowToPlay), new Color(0f, 0f, 0f, 0f), Palette.TextMuted, 34);
            how.GetComponent<RectTransform>().Anchor(0.3f, 0.01f, 0.7f, 0.055f);
        }

        /// <summary>Botão da tela inicial. <paramref name="half"/>: -1 = largura toda, 0 = metade esquerda, 1 = metade direita.</summary>
        private static void AddHomeButton(RectTransform page, string label, int index, bool primary, Action onClick, int half = -1)
        {
            const float top = 0.49f, height = 0.07f, gap = 0.018f;
            float yMax = top - index * (height + gap);
            var b = UiKit.Button("Botao_" + label, page, label, onClick,
                                 primary ? Palette.Button : Palette.ButtonSecondary, primary ? Palette.ButtonText : Palette.Text, 50);
            float xMin = half == 1 ? 0.51f : 0.1f, xMax = half == 0 ? 0.49f : 0.9f;
            b.GetComponent<RectTransform>().Anchor(xMin, yMax - height, xMax, yMax);
        }

        private void BuildOffline()
        {
            var page = NewPage(Page.Offline, "Jogar offline");
            offlineRules = RulesEditor.Create(page, RulesEditor.Mode.Offline);
            ((RectTransform)offlineRules.transform).Anchor(0.04f, 0.13f, 0.96f, 0.915f);
            Footer(page, "Começar partida", SubmitOffline);
        }

        private void BuildCreateRoom()
        {
            var page = NewPage(Page.CreateRoom, "Criar sala");
            roomRules = RulesEditor.Create(page, RulesEditor.Mode.Room);
            ((RectTransform)roomRules.transform).Anchor(0.04f, 0.13f, 0.96f, 0.915f);
            Footer(page, "Criar sala", SubmitCreateRoom);
        }

        private void BuildJoinRoom()
        {
            var page = NewPage(Page.JoinRoom, "Entrar com código");
            var label = UiKit.Label("Pergunta", page, "Digite o código da sala", 44, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            label.rectTransform.Anchor(0.06f, 0.7f, 0.94f, 0.77f);

            codeField = UiControls.TextField("Codigo", page, "ABC123", 90, RoomCode.Length + 2);
            codeField.textComponent.alignment = TextAnchor.MiddleCenter;
            ((Text)codeField.placeholder).alignment = TextAnchor.MiddleCenter;
            codeField.GetComponent<RectTransform>().Anchor(0.12f, 0.57f, 0.88f, 0.68f);
            codeField.onValueChanged.AddListener(OnCodeChanged);

            codeHint = UiKit.Label("Dica", page, "", 34, Palette.TextMuted, TextAnchor.MiddleCenter, FontStyle.Normal, true);
            codeHint.rectTransform.Anchor(0.06f, 0.5f, 0.94f, 0.56f);

            joinButton = Footer(page, "Entrar", SubmitJoin);
        }

        private void BuildPublicRooms()
        {
            var page = NewPage(Page.PublicRooms, "Salas públicas");
            roomsList = UiControls.ScrollArea("Lista", page, 16f);
            ((RectTransform)roomsList.parent).Anchor(0.04f, 0.13f, 0.96f, 0.915f);
            roomsEmpty = UiKit.Label("Vazio", page, "Nenhuma sala pública no momento.\nCrie a sua e chame os amigos!", 40,
                                     Palette.TextMuted, TextAnchor.MiddleCenter, FontStyle.Normal, true);
            roomsEmpty.rectTransform.Anchor(0.08f, 0.45f, 0.92f, 0.6f);
            Footer(page, "Atualizar", () => RefreshRoomsRequested?.Invoke());
        }

        private void BuildSettings()
        {
            var page = NewPage(Page.Settings, "Configurações");
            var content = UiControls.ScrollArea("Itens", page, 18f);
            ((RectTransform)content.parent).Anchor(0.04f, 0.13f, 0.96f, 0.915f);

            var nameLabel = UiKit.Label("RotuloNome", content, "Seu nome", 36, Palette.Text, TextAnchor.LowerLeft, FontStyle.Bold);
            UiControls.SetHeight(nameLabel, 60f);
            nameField = UiControls.TextField("Nome", content, "Jogador", 46, GamePrefs.MaxNameLength);
            UiControls.SetHeight(nameField, 110f);
            nameField.onEndEdit.AddListener(value =>
            {
                GamePrefs.PlayerName = value;
                if (!string.IsNullOrWhiteSpace(value)) nameField.text = GamePrefs.PlayerName;
            });

            soundStepper = OptionStepper.Create(content, "Sons", new[] { "Desligados", "Ligados" }, 1);
            soundStepper.Changed += i => GamePrefs.SoundOn = i == 1;
            vibrationStepper = OptionStepper.Create(content, "Vibração", new[] { "Desligada", "Ligada" }, 1);
            vibrationStepper.Changed += i => GamePrefs.VibrationOn = i == 1;

            var how = UiKit.Button("ComoJogar", content, "Como jogar", () => ShowPage(Page.HowToPlay), Palette.ButtonSecondary, Palette.Text, 40);
            UiControls.SetHeight(how, 104f);

            BuildVipSection(content);

            var serverLabel = UiKit.Label("RotuloServidor", content, "Servidor (avançado)", 32, Palette.TextMuted, TextAnchor.LowerLeft, FontStyle.Bold);
            UiControls.SetHeight(serverLabel, 56f);
            serverField = UiControls.TextField("Servidor", content, GamePrefs.DefaultServerUrl, 30, 120);
            UiControls.SetHeight(serverField, 90f);
            serverField.onEndEdit.AddListener(value =>
            {
                GamePrefs.ServerUrl = value;
                serverField.text = GamePrefs.HasCustomServer ? GamePrefs.ServerUrl : "";
            });

            var version = UiKit.Label("Versao", content, "Fodinha Online · versão " + Application.version, 30, Palette.TextMuted,
                                      TextAnchor.MiddleCenter, FontStyle.Normal);
            UiControls.SetHeight(version, 80f);
        }

        private void BuildHowToPlay()
        {
            var page = NewPage(Page.HowToPlay, "Como jogar");
            var content = UiControls.ScrollArea("Texto", page, 0f);
            ((RectTransform)content.parent).Anchor(0.05f, 0.02f, 0.95f, 0.915f);
            var text = UiKit.Label("Regras", content, HowToPlayText, 38, Palette.Text, TextAnchor.UpperLeft);
            text.verticalOverflow = VerticalWrapMode.Overflow; // altura vem do layout (preferred height do texto)
        }

        private const string HowToPlayText =
            "<b><color=#FFC83D>Objetivo</color></b>\nSer o último jogador com vidas. Cada um começa com 3 vidas.\n\n" +
            "<b><color=#FFC83D>Cartas</color></b>\nA 1ª mão tem 1 carta e é cega; a cada mão, 1 carta a mais, até o máximo que o baralho permite (depois mantém).\n\n" +
            "<b><color=#FFC83D>Apostas</color></b>\nDepois de ver as cartas, cada um tem 30 segundos para dizer quantas rodadas vai fazer. O último a apostar (o pé) não pode dizer o número que deixa a soma igual ao número de cartas.\n\n" +
            "<b><color=#FFC83D>Rodadas</color></b>\nCada um joga uma carta; a mais forte leva a rodada. Força: 4 5 6 7 Q J K A 2 3. Se duas cartas tiverem o mesmo valor, o naipe desempata: Paus > Copas > Espadas > Ouros.\n\n" +
            "<b><color=#FFC83D>Manilha</color></b>\nA carta do meio da mesa (vira) define a manilha: o valor seguinte. Manilha vence qualquer carta; entre manilhas vale o naipe.\n\n" +
            "<b><color=#FFC83D>Vidas</color></b>\nQuem faz exatamente o que apostou não perde nada. Quem erra perde 1 vida. Sem vidas, está fora.\n\n" +
            "<b><color=#FFC83D>Mão cega</color></b>\nNa mão de 1 carta você vê a carta dos outros, mas não a sua!\n\n" +
            "<b><color=#FFC83D>Tempo e conexão</color></b>\nSe o tempo acabar, o bot joga por você. Se a internet cair, você tem 5 minutos para voltar; enquanto isso o bot joga no seu lugar.";

        // ------------------------------------------------------------------ ações

        private void SubmitOffline()
        {
            var rules = offlineRules.BuildRules();
            var errors = rules.GetValidationErrors();
            if (errors.Count > 0) { ShowToast(errors[0]); return; }

            GamePrefs.OfflineRules = rules;
            GamePrefs.OfflineBots = offlineRules.PlayerOrBotCount;
            OfflineRequested?.Invoke(new OfflineMatchConfig
            {
                PlayerName = GamePrefs.PlayerName,
                BotCount = offlineRules.PlayerOrBotCount,
                Rules = rules
            });
        }

        private void SubmitCreateRoom()
        {
            var options = new RoomOptions
            {
                IsPublic = roomRules.IsPublic,
                MaxPlayers = roomRules.PlayerOrBotCount,
                Stake = roomRules.Stake,
                Rules = roomRules.BuildRules()
            };
            var errors = options.GetValidationErrors();
            if (errors.Count > 0) { ShowToast(errors[0]); return; }
            GamePrefs.LastRoomOptions = options;
            CreateRoomRequested?.Invoke(options);
        }

        private void OnCodeChanged(string value)
        {
            var normalized = RoomCode.Normalize(value);
            if (normalized.Length > RoomCode.Length) normalized = normalized.Substring(0, RoomCode.Length);
            if (normalized != value) codeField.SetTextWithoutNotify(normalized);

            bool valid = RoomCode.IsValid(normalized);
            joinButton.interactable = valid;
            if (normalized.Length == 0) codeHint.text = "6 letras e números, sem 0, O, 1, I e L";
            else if (normalized.Length < RoomCode.Length)
            {
                int missing = RoomCode.Length - normalized.Length;
                codeHint.text = missing == 1 ? "Falta 1 caractere" : "Faltam " + missing + " caracteres";
            }
            else codeHint.text = valid ? UiKit.Colored("Código válido", Palette.Good) : UiKit.Colored("Código com caracteres inválidos", Palette.Bad);
        }

        private void SubmitJoin()
        {
            var code = RoomCode.Normalize(codeField.text);
            if (!RoomCode.IsValid(code)) { ShowToast("Código inválido"); return; }
            JoinRoomRequested?.Invoke(code);
        }

        // ------------------------------------------------------------------ chamado pelo MenuRoot

        public void SetPublicRooms(IReadOnlyList<RoomListing> rooms)
        {
            EnsureBuilt();
            UiKit.DestroyChildren(roomsList);
            ListedRooms = rooms?.Count ?? 0;
            roomsEmpty.gameObject.SetActive(ListedRooms == 0);
            if (rooms == null) return;

            foreach (var room in rooms)
            {
                var row = UiKit.Image("Sala_" + room.Code, roomsList, Palette.Panel, SpriteFactory.RoundedRect, 1.5f);
                UiControls.SetHeight(row, 150f);
                var name = UiKit.Label("Nome", row.transform, "Mesa de " + room.HostName, 40, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Bold, true);
                name.rectTransform.Anchor(0.04f, 0.5f, 0.66f, 0.92f);
                string status = room.InProgress ? "em jogo" : room.IsFull ? "cheia" : "aguardando";
                var info = UiKit.Label("Info", row.transform, room.Players + "/" + room.MaxPlayers + " jogadores · " + (room.Stake > 0 ? "valendo " + room.Stake + " moedas" : "grátis") + " · " + status,
                                       30, Palette.TextMuted, TextAnchor.MiddleLeft, FontStyle.Normal, true);
                info.rectTransform.Anchor(0.04f, 0.1f, 0.66f, 0.5f);
                string code = room.Code;
                var join = UiKit.Button("Entrar", row.transform, room.CanJoin ? "Entrar" : status, () => JoinRoomRequested?.Invoke(code),
                                        Palette.Button, Palette.ButtonText, 40);
                join.GetComponent<RectTransform>().Anchor(0.69f, 0.18f, 0.96f, 0.82f);
                join.interactable = room.CanJoin;
            }
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

        // ------------------------------------------------------------------ moedas e ranking

        private Text coinsText;
        private Button claimButton;
        private RectTransform rankingList;
        private Text rankingMine;

        /// <summary>Uma linha do ranking (dados simples, vindos da camada online).</summary>
        public struct RankingRow
        {
            public int Position;
            public string Name;
            public int Coins;
            public int Wins;
        }

        public int? ShownCoins { get; private set; }
        public bool ClaimVisible => claimButton != null && claimButton.gameObject.activeSelf;
        public int ListedRanking { get; private set; }

        private void BuildWalletRow(RectTransform page)
        {
            var badge = UiKit.Image("Carteira", page, Palette.Panel, SpriteFactory.RoundedRect, 1.5f);
            badge.rectTransform.Anchor(0.04f, 0.948f, 0.4f, 0.992f);
            var coin = UiKit.Image("Moeda", badge.transform, Palette.Accent, SpriteFactory.Circle);
            coin.rectTransform.Anchor(0.04f, 0.15f, 0.16f, 0.85f);
            coin.preserveAspect = true;
            var c = UiKit.Label("M", coin.transform, "$", 40, Palette.AccentDark, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            c.rectTransform.Fill();
            coinsText = UiKit.Label("Moedas", badge.transform, "—", 38, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Bold, true);
            coinsText.rectTransform.Anchor(0.2f, 0.08f, 0.97f, 0.92f);

            // "+100 moedas" fica sempre à vista; com 500 ou mais ele fica apagado e explica quando libera.
            claimButton = UiKit.Button("Ganhar", page, "+100 moedas", OnClaimClicked, Palette.Good, Palette.ButtonText, 34);
            claimButton.GetComponent<RectTransform>().Anchor(0.42f, 0.948f, 0.7f, 0.992f);
            claimButton.gameObject.SetActive(false);

            var rank = UiKit.Button("Ranking", page, "Ranking", () => ShowPage(Page.Ranking), Palette.ButtonSecondary, Palette.Text, 34);
            rank.GetComponent<RectTransform>().Anchor(0.72f, 0.948f, 0.96f, 0.992f);
        }

        /// <summary>Mínimo de moedas abaixo do qual o vídeo libera +100 (mesmo valor do servidor).</summary>
        public const int RewardLimit = 500;

        private bool canClaimNow;

        /// <summary>Pode tocar no "+100 moedas" agora (tem menos de 500).</summary>
        public bool ClaimAvailable => ClaimVisible && canClaimNow;

        private void OnClaimClicked()
        {
            if (!ShownCoins.HasValue) { ShowToast("Sem conexão com o servidor. Tente de novo em instantes."); return; }
            if (!canClaimNow)
            {
                ShowToast("O vídeo de +100 moedas libera quando você tiver menos de " + RewardLimit + " moedas.", 3.5f);
                return;
            }
            ClaimRewardRequested?.Invoke();
        }

        /// <summary>Mostra o saldo (null = sem conexão) e o botão de ganhar moedas.</summary>
        public void SetCoins(int? coins, bool canClaim)
        {
            EnsureBuilt();
            ShownCoins = coins;
            canClaimNow = canClaim;
            coinsText.text = coins.HasValue ? FormatCoins(coins.Value) : "—";
            claimButton.gameObject.SetActive(coins.HasValue);
            claimButton.image.color = canClaim ? Palette.Good : Palette.Disabled;
            UiKit.ButtonLabel(claimButton).color = canClaim ? Palette.ButtonText : Palette.TextMuted;
            if (CurrentPage == Page.Shop) RefreshShop();
        }

        public static string FormatCoins(int coins) => coins.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

        private void BuildRanking()
        {
            var page = NewPage(Page.Ranking, "Ranking");
            rankingList = UiControls.ScrollArea("Lista", page, 10f);
            ((RectTransform)rankingList.parent).Anchor(0.04f, 0.2f, 0.96f, 0.915f);
            rankingMine = UiKit.Label("Minha", page, "", 38, Palette.Accent, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            rankingMine.rectTransform.Anchor(0.05f, 0.12f, 0.95f, 0.19f);
            Footer(page, "Atualizar", () => RankingRequested?.Invoke());
        }

        public void ShowRanking(IReadOnlyList<RankingRow> rows, int myPosition, int myCoins)
        {
            EnsureBuilt();
            UiKit.DestroyChildren(rankingList);
            ListedRanking = rows?.Count ?? 0;
            if (rows != null)
            {
                foreach (var r in rows)
                {
                    var row = UiKit.Image("Pos" + r.Position, rankingList, Palette.Panel, SpriteFactory.RoundedRect, 1.5f);
                    UiControls.SetHeight(row, 92f);
                    Color medal = r.Position == 1 ? Palette.Accent : r.Position == 2 ? Palette.Hex("C9D1D9") : r.Position == 3 ? Palette.Hex("D08B4F") : Palette.TextMuted;
                    var pos = UiKit.Label("Posicao", row.transform, r.Position + "º", 40, medal, TextAnchor.MiddleCenter, FontStyle.Bold, true);
                    pos.rectTransform.Anchor(0.02f, 0.1f, 0.14f, 0.9f);
                    var name = UiKit.Label("Nome", row.transform, r.Name, 38, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Bold, true);
                    name.rectTransform.Anchor(0.16f, 0.1f, 0.6f, 0.9f);
                    var coins = UiKit.Label("Moedas", row.transform, FormatCoins(r.Coins) + UiKit.Colored("  moedas", Palette.TextMuted),
                                            34, Palette.Accent, TextAnchor.MiddleRight, FontStyle.Bold, true);
                    coins.rectTransform.Anchor(0.6f, 0.4f, 0.97f, 0.95f);
                    var wins = UiKit.Label("Vitorias", row.transform, r.Wins + (r.Wins == 1 ? " vitória" : " vitórias"),
                                           26, Palette.TextMuted, TextAnchor.MiddleRight, FontStyle.Normal, true);
                    wins.rectTransform.Anchor(0.6f, 0.05f, 0.97f, 0.42f);
                }
            }
            rankingMine.text = myPosition > 0
                ? "Você está em " + myPosition + "º com " + FormatCoins(myCoins) + " moedas"
                : "Jogue online para entrar no ranking";
        }

        /// <summary>Tela de "aguarde" (conectando, criando sala...).</summary>
        public void SetBusy(bool busy, string message = "")
        {
            EnsureBuilt();
            busyOverlay.SetActive(busy);
            busyText.text = message;
            if (busy) busyOverlay.transform.SetAsLastSibling();
        }
    }
}
