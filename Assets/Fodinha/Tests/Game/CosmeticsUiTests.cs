using System.Collections.Generic;
using Fodinha.Game.Menu;
using Fodinha.Game.UI;
using Fodinha.Hosting;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Fodinha.Game.Tests
{
    /// <summary>Avatar, loja e código VIP no app. Guarda e restaura as preferências do aparelho.</summary>
    public class CosmeticsUiTests
    {
        private static readonly string[] Keys =
            { "fodinha.avatar", "fodinha.mesa", "fodinha.baralho", "fodinha.itens", "fodinha.semanuncios", "fodinha.nome" };

        private readonly Dictionary<string, string> savedPrefs = new Dictionary<string, string>();
        private readonly Dictionary<string, int> savedInts = new Dictionary<string, int>();
        private GameObject canvasGo;
        private MenuScreen menu;

        [SetUp]
        public void SetUp()
        {
            foreach (var k in Keys)
            {
                if (!PlayerPrefs.HasKey(k)) continue;
                savedPrefs[k] = PlayerPrefs.GetString(k, null);
                savedInts[k] = PlayerPrefs.GetInt(k, int.MinValue);
            }
            foreach (var k in Keys) PlayerPrefs.DeleteKey(k);

            canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            menu = UiKit.Rect("Menu", canvasGo.transform).Fill().gameObject.AddComponent<MenuScreen>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(canvasGo);
            foreach (var k in Keys) PlayerPrefs.DeleteKey(k);
            foreach (var kv in savedPrefs)
            {
                if (kv.Key == "fodinha.semanuncios") PlayerPrefs.SetInt(kv.Key, savedInts[kv.Key]);
                else PlayerPrefs.SetString(kv.Key, kv.Value);
            }
            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------------ desenho

        [Test]
        public void Avatar_DesenhaCirculoComRosto_EGuardaNaMemoria()
        {
            var spec = new AvatarSpec { [AvatarSpec.Part.Hair] = 4, [AvatarSpec.Part.Beard] = 3 };
            var tex = AvatarPainter.Paint(spec, 64, keepReadable: true);
            Assert.That(tex.GetPixel(0, 0).a, Is.EqualTo(0f).Within(0.01f), "Canto fora do círculo é transparente.");
            Assert.That(tex.GetPixel(32, 32).a, Is.EqualTo(1f).Within(0.01f), "Centro é opaco.");
            Object.DestroyImmediate(tex);

            var a = AvatarPainter.Get(spec);
            var b = AvatarPainter.Get(spec.Encode());
            Assert.That(b, Is.SameAs(a), "O mesmo avatar não é desenhado duas vezes.");
            Assert.That(AvatarPainter.Get("lixo", "Zé"), Is.SameAs(AvatarPainter.Get(AvatarSpec.FromSeed("Zé"))));
        }

        [Test]
        public void TodasAsOpcoes_DesenhamSemErro()
        {
            for (int part = 0; part < AvatarSpec.PartCount; part++)
            for (int option = 0; option < AvatarSpec.OptionCount((AvatarSpec.Part)part); option++)
            {
                var spec = new AvatarSpec();
                spec[(AvatarSpec.Part)part] = option;
                var tex = AvatarPainter.Paint(spec, 32);
                Assert.That(tex.width, Is.EqualTo(32));
                Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void MesasEBaralhos_TemDesenhoProprio()
        {
            var tables = new HashSet<Sprite>();
            var decks = new HashSet<Sprite>();
            foreach (var item in ShopCatalog.Items)
            {
                if (item.Category == ShopCategory.Table) tables.Add(SpriteFactory.TableFor(item.Id));
                else decks.Add(SpriteFactory.CardBackFor(item.Id));
            }
            Assert.That(tables.Count, Is.EqualTo(ShopCatalog.Of(ShopCategory.Table).Count));
            Assert.That(decks.Count, Is.EqualTo(ShopCatalog.Of(ShopCategory.Deck).Count));
            Assert.That(SpriteFactory.TableFor("mesa_azul"), Is.SameAs(SpriteFactory.TableFor("mesa_azul")));
        }

        // ------------------------------------------------------------------ preferências

        [Test]
        public void Preferencias_SoAceitamItensQueOJogadorTem()
        {
            Assert.That(AvatarSpec.TryParse(GamePrefs.Avatar, out _), Is.True, "Na 1ª vez sorteia um avatar válido.");
            Assert.That(GamePrefs.Avatar, Is.EqualTo(GamePrefs.Avatar), "E guarda o mesmo.");

            GamePrefs.TableTheme = "mesa_cassino";
            Assert.That(GamePrefs.TableTheme, Is.EqualTo(ShopCatalog.DefaultTable), "Não comprou: continua a padrão.");

            GamePrefs.ApplyServerProfile(new List<string> { "mesa_cassino", "baralho_rosa" }, "mesa_cassino", "baralho_rosa", true);
            Assert.That(GamePrefs.TableTheme, Is.EqualTo("mesa_cassino"));
            Assert.That(GamePrefs.DeckTheme, Is.EqualTo("baralho_rosa"));
            Assert.That(GamePrefs.NoAds, Is.True);
            Assert.That(SpriteFactory.Table, Is.SameAs(SpriteFactory.TableFor("mesa_cassino")));
            Assert.That(SpriteFactory.CardBack, Is.SameAs(SpriteFactory.CardBackFor("baralho_rosa")));
        }

        [Test]
        public void SemAnuncios_PulaOsAnuncios_EGanhaDireto()
        {
            GamePrefs.NoAds = true;
            bool rewarded = false, closed = false;
            Ads.AdService.ShowRewarded(earned => rewarded = earned);
            Ads.AdService.ShowInterstitial(() => closed = true);
            Assert.That(rewarded, Is.True);
            Assert.That(closed, Is.True);
        }

        // ------------------------------------------------------------------ telas

        [Test]
        public void EditorDeAvatar_MudaPrevia_ESalva()
        {
            string saved = null;
            menu.AvatarSaved += a => saved = a;
            menu.ShowPage(MenuScreen.Page.Avatar);
            Assert.That(menu.AvatarSteppers.Count, Is.EqualTo(AvatarSpec.PartCount));

            var hair = menu.AvatarSteppers[(int)AvatarSpec.Part.Hair];
            hair.SetIndex(0);
            hair.Step(+1);
            Assert.That(menu.AvatarDraft[AvatarSpec.Part.Hair], Is.EqualTo(1));

            menu.transform.Find("AreaSegura/Pagina_Avatar/Acao").GetComponent<Button>().onClick.Invoke();
            Assert.That(saved, Is.EqualTo(menu.AvatarDraft.Encode()));
            Assert.That(GamePrefs.Avatar, Is.EqualTo(saved));
            Assert.That(menu.CurrentPage, Is.EqualTo(MenuScreen.Page.Home));
        }

        [Test]
        public void Loja_ListaItens_CompraPedeConfirmacao_EUsarTroca()
        {
            menu.SetCoins(1000, false);
            menu.ShowPage(MenuScreen.Page.Shop);
            Assert.That(menu.ListedShopItems, Is.EqualTo(ShopCatalog.Of(ShopCategory.Table).Count));

            var list = menu.transform.Find("AreaSegura/Pagina_Shop/Lista/Conteudo");
            var free = list.Find("Item_" + ShopCatalog.DefaultTable + "/Acao").GetComponent<Button>();
            Assert.That(free.interactable, Is.False, "A mesa em uso não tem o que fazer.");

            string bought = null;
            menu.BuyRequested += id => bought = id;
            list.Find("Item_mesa_azul/Acao").GetComponent<Button>().onClick.Invoke();
            Assert.That(bought, Is.Null, "Primeiro pergunta.");
            var dialog = menu.transform.Find("AreaSegura/Dialogo");
            Assert.That(dialog, Is.Not.Null);
            dialog.Find("Caixa/Confirmar").GetComponent<Button>().onClick.Invoke();
            Assert.That(bought, Is.EqualTo("mesa_azul"));

            // Servidor confirmou a compra: passa a aparecer "Usar"/"Usando".
            GamePrefs.ApplyServerProfile(new List<string> { "mesa_azul" }, ShopCatalog.DefaultTable, ShopCatalog.DefaultDeck, false);
            menu.RefreshCosmetics();
            list = menu.transform.Find("AreaSegura/Pagina_Shop/Lista/Conteudo");
            string equipped = null;
            menu.EquipRequested += id => equipped = id;
            list.Find("Item_mesa_azul/Acao").GetComponent<Button>().onClick.Invoke();
            Assert.That(equipped, Is.EqualTo("mesa_azul"));
            Assert.That(GamePrefs.TableTheme, Is.EqualTo("mesa_azul"));

            menu.SetShopTab(ShopCategory.Deck);
            Assert.That(menu.ListedShopItems, Is.EqualTo(ShopCatalog.Of(ShopCategory.Deck).Count));
        }

        [Test]
        public void Loja_SemMoedasSuficientes_Avisa()
        {
            menu.SetCoins(100, true);
            menu.ShowPage(MenuScreen.Page.Shop);
            string bought = null;
            menu.BuyRequested += id => bought = id;
            menu.transform.Find("AreaSegura/Pagina_Shop/Lista/Conteudo/Item_mesa_azul/Acao").GetComponent<Button>().onClick.Invoke();
            Assert.That(bought, Is.Null);
            Assert.That(menu.LastToast, Does.Contain("Faltam 200"));
        }

        [Test]
        public void CodigoVip_EnviaOCodigo()
        {
            string code = null;
            menu.VipCodeSubmitted += c => code = c;
            menu.ShowPage(MenuScreen.Page.Settings);
            menu.VipField.text = "  meu-codigo ";
            menu.transform.Find("AreaSegura/Pagina_Settings/Itens/Conteudo/AtivarVip").GetComponent<Button>().onClick.Invoke();
            Assert.That(code, Is.EqualTo("meu-codigo"));
        }
    }
}
