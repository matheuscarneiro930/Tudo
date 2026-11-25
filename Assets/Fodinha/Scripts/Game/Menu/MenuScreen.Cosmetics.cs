using System;
using System.Collections.Generic;
using Fodinha.Game.UI;
using Fodinha.Hosting;
using UnityEngine;
using UnityEngine.UI;

namespace Fodinha.Game.Menu
{
    /// <summary>Menu: avatar, loja de mesas/baralhos (moedas) e código VIP (sem anúncios).</summary>
    public sealed partial class MenuScreen
    {
        private AvatarView homeAvatar;

        // Editor de avatar
        private AvatarView avatarPreview;
        private AvatarSpec avatarDraft;
        private readonly List<OptionStepper> avatarSteppers = new List<OptionStepper>();
        private readonly System.Random avatarRandom = new System.Random();

        // Loja
        private ShopCategory shopTab = ShopCategory.Table;
        private RectTransform shopList;
        private Text shopCoins;
        private Button tableTab;
        private Button deckTab;

        // Código VIP
        private InputField vipField;
        private Text vipStatus;

        /// <summary>Avatar salvo (texto do AvatarSpec): a camada online envia ao servidor.</summary>
        public event Action<string> AvatarSaved;
        /// <summary>Comprar um item (id do <see cref="ShopCatalog"/>): só o servidor decide.</summary>
        public event Action<string> BuyRequested;
        /// <summary>Passar a usar um item que já é do jogador.</summary>
        public event Action<string> EquipRequested;
        /// <summary>Código VIP digitado nas Configurações.</summary>
        public event Action<string> VipCodeSubmitted;

        public AvatarSpec AvatarDraft => avatarDraft;
        public IReadOnlyList<OptionStepper> AvatarSteppers => avatarSteppers;
        public ShopCategory ShopTab => shopTab;
        public int ListedShopItems { get; private set; }
        public InputField VipField => vipField;

        // ------------------------------------------------------------------ avatar

        private void BuildAvatarPage()
        {
            var page = NewPage(Page.Avatar, "Seu avatar");

            var glow = UiKit.Image("Brilho", page, new Color(1f, 1f, 1f, 0.18f), SpriteFactory.SoftGlow);
            glow.rectTransform.Anchor(0.15f, 0.62f, 0.85f, 0.93f);
            glow.raycastTarget = false;
            avatarPreview = AvatarView.Create(page, "Previa");
            avatarPreview.Image.rectTransform.Anchor(0.27f, 0.66f, 0.73f, 0.905f);

            var random = UiKit.Button("Sortear", page, "Sortear", () =>
            {
                avatarDraft = AvatarSpec.Random(avatarRandom);
                SyncAvatarSteppers();
            }, Palette.ButtonSecondary, Palette.Text, 36);
            random.GetComponent<RectTransform>().Anchor(0.32f, 0.605f, 0.68f, 0.65f);

            var content = UiControls.ScrollArea("Partes", page, 12f);
            ((RectTransform)content.parent).Anchor(0.04f, 0.13f, 0.96f, 0.595f);
            avatarSteppers.Clear();
            for (int i = 0; i < AvatarSpec.PartCount; i++)
            {
                var part = (AvatarSpec.Part)i;
                var stepper = OptionStepper.Create(content, AvatarSpec.PartLabels[i], AvatarSpec.OptionLabels[i], 0);
                stepper.Changed += index =>
                {
                    avatarDraft[part] = index;
                    avatarPreview.ShowPreview(avatarDraft);
                };
                avatarSteppers.Add(stepper);
            }

            Footer(page, "Salvar avatar", SaveAvatar);
        }

        private void LoadAvatarEditor()
        {
            avatarDraft = AvatarSpec.ParseOrSeed(GamePrefs.Avatar, GamePrefs.PlayerName);
            SyncAvatarSteppers();
        }

        private void SyncAvatarSteppers()
        {
            for (int i = 0; i < avatarSteppers.Count; i++) avatarSteppers[i].SetIndex(avatarDraft[(AvatarSpec.Part)i]);
            avatarPreview.ShowPreview(avatarDraft);
        }

        private void SaveAvatar()
        {
            var encoded = avatarDraft.Encode();
            GamePrefs.Avatar = encoded;
            homeAvatar.Show(encoded);
            AvatarSaved?.Invoke(encoded);
            ShowPage(Page.Home);
            ShowToast("Avatar salvo!");
        }

        // ------------------------------------------------------------------ loja

        private void BuildShop()
        {
            var page = NewPage(Page.Shop, "Loja");

            shopCoins = UiKit.Label("Saldo", page, "", 38, Palette.Accent, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            shopCoins.rectTransform.Anchor(0.05f, 0.865f, 0.95f, 0.915f);

            tableTab = UiKit.Button("AbaMesas", page, "Mesas", () => SetShopTab(ShopCategory.Table), Palette.Button, Palette.ButtonText, 40);
            tableTab.GetComponent<RectTransform>().Anchor(0.05f, 0.795f, 0.49f, 0.855f);
            deckTab = UiKit.Button("AbaBaralhos", page, "Baralhos", () => SetShopTab(ShopCategory.Deck), Palette.ButtonSecondary, Palette.Text, 40);
            deckTab.GetComponent<RectTransform>().Anchor(0.51f, 0.795f, 0.95f, 0.855f);

            shopList = UiControls.ScrollArea("Lista", page, 14f);
            ((RectTransform)shopList.parent).Anchor(0.04f, 0.03f, 0.96f, 0.785f);
        }

        public void SetShopTab(ShopCategory tab)
        {
            EnsureBuilt();
            shopTab = tab;
            RefreshShop();
        }

        /// <summary>Redesenha a loja com o saldo e os itens atuais (chamado também quando chega o perfil do servidor).</summary>
        public void RefreshShop()
        {
            EnsureBuilt();
            bool tables = shopTab == ShopCategory.Table;
            SetTabStyle(tableTab, tables);
            SetTabStyle(deckTab, !tables);
            shopCoins.text = ShownCoins.HasValue ? "Você tem " + FormatCoins(ShownCoins.Value) + " moedas" : "Conecte-se para comprar";

            UiKit.DestroyChildren(shopList);
            var owned = GamePrefs.OwnedItems;
            string equipped = tables ? GamePrefs.TableTheme : GamePrefs.DeckTheme;
            var items = ShopCatalog.Of(shopTab);
            ListedShopItems = items.Count;
            foreach (var item in items) BuildShopRow(item, ShopCatalog.Owns(owned, item.Id), item.Id == equipped);
        }

        private static void SetTabStyle(Button tab, bool active)
        {
            tab.image.color = active ? Palette.Button : Palette.ButtonSecondary;
            UiKit.ButtonLabel(tab).color = active ? Palette.ButtonText : Palette.Text;
        }

        private void BuildShopRow(ShopItem item, bool owned, bool equipped)
        {
            var row = UiKit.Image("Item_" + item.Id, shopList, equipped ? new Color(1f, 0.78f, 0.24f, 0.18f) : Palette.Panel, SpriteFactory.RoundedRect, 1.5f);
            UiControls.SetHeight(row, 210f);

            var preview = UiKit.Rect("Previa", row.transform).Anchor(0.02f, 0.06f, 0.36f, 0.94f);
            if (item.Category == ShopCategory.Table)
            {
                var felt = UiKit.Image("Mesa", preview, Color.white, SpriteFactory.TableThumbFor(item.Id));
                felt.rectTransform.Anchor(0.04f, 0.1f, 0.96f, 0.9f);
                felt.raycastTarget = false;
            }
            else
            {
                for (int i = 0; i < 2; i++)
                {
                    var card = UiKit.Image("Carta" + i, preview, Color.white, SpriteFactory.CardBackFor(item.Id));
                    card.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(110f, 160f), new Vector2(i == 0 ? -28f : 28f, 0f));
                    card.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 10f : -8f);
                    card.raycastTarget = false;
                }
            }

            var name = UiKit.Label("Nome", row.transform, item.Name, 40, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Bold, true);
            name.rectTransform.Anchor(0.39f, 0.52f, 0.97f, 0.9f);
            string info = equipped ? UiKit.Colored("Em uso", Palette.Good)
                        : owned ? (item.IsFree ? "Grátis" : "Comprado")
                        : FormatCoins(item.Price) + " moedas";
            var price = UiKit.Label("Preco", row.transform, info, 32, owned ? Palette.TextMuted : Palette.Accent, TextAnchor.MiddleLeft, FontStyle.Bold, true);
            price.rectTransform.Anchor(0.39f, 0.12f, 0.64f, 0.5f);

            string id = item.Id;
            Button action;
            if (equipped)
            {
                action = UiKit.Button("Acao", row.transform, "Usando", null, Palette.ButtonSecondary, Palette.Text, 34);
                action.interactable = false;
            }
            else if (owned) action = UiKit.Button("Acao", row.transform, "Usar", () => Equip(id), Palette.ButtonSecondary, Palette.Text, 36);
            else action = UiKit.Button("Acao", row.transform, "Comprar", () => ConfirmBuy(item), Palette.Button, Palette.ButtonText, 36);
            action.GetComponent<RectTransform>().Anchor(0.66f, 0.12f, 0.97f, 0.5f);
        }

        private void Equip(string id)
        {
            var item = ShopCatalog.Find(id);
            if (item == null) return;
            if (item.Category == ShopCategory.Table) GamePrefs.TableTheme = id;
            else GamePrefs.DeckTheme = id;
            EquipRequested?.Invoke(id);
            RefreshShop();
            ShowToast(item.Name + " em uso!");
        }

        private void ConfirmBuy(ShopItem item)
        {
            if (!ShownCoins.HasValue)
            {
                ShowToast("Sem conexão com o servidor. A loja precisa da internet.");
                return;
            }
            if (ShownCoins.Value < item.Price)
            {
                ShowToast("Faltam " + FormatCoins(item.Price - ShownCoins.Value) + " moedas para comprar " + item.Name + ".");
                return;
            }
            string kind = item.Category == ShopCategory.Table ? "a mesa" : "o baralho";
            UiControls.Confirm(safeRoot, "Comprar?", "Comprar " + kind + " " + item.Name + " por " + FormatCoins(item.Price) + " moedas?",
                               "Comprar", () => BuyRequested?.Invoke(item.Id));
        }

        /// <summary>O perfil do servidor mudou (itens, mesa, baralho, avatar): atualiza o que estiver na tela.</summary>
        public void RefreshCosmetics()
        {
            EnsureBuilt();
            homeAvatar.Show(GamePrefs.Avatar);
            if (CurrentPage == Page.Shop) RefreshShop();
            if (CurrentPage == Page.Settings) RefreshVipStatus();
        }

        // ------------------------------------------------------------------ código VIP

        private void BuildVipSection(RectTransform content)
        {
            var label = UiKit.Label("RotuloVip", content, "Código VIP (sem anúncios)", 32, Palette.TextMuted, TextAnchor.LowerLeft, FontStyle.Bold);
            UiControls.SetHeight(label, 56f);
            vipField = UiControls.TextField("CodigoVip", content, "Digite o código", 36, 64);
            UiControls.SetHeight(vipField, 96f);
            var activate = UiKit.Button("AtivarVip", content, "Ativar código", () =>
            {
                var code = vipField.text;
                if (string.IsNullOrWhiteSpace(code)) { ShowToast("Digite o código."); return; }
                vipField.text = "";
                VipCodeSubmitted?.Invoke(code.Trim());
            }, Palette.ButtonSecondary, Palette.Text, 36);
            UiControls.SetHeight(activate, 96f);
            vipStatus = UiKit.Label("StatusVip", content, "", 30, Palette.Good, TextAnchor.MiddleLeft, FontStyle.Bold, true);
            UiControls.SetHeight(vipStatus, 56f);
        }

        private void RefreshVipStatus()
        {
            vipStatus.text = GamePrefs.NoAds ? "Sua conta está sem anúncios." : "";
        }
    }
}
