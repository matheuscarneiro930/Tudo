using System.Collections.Generic;
using Fodinha.Core;
using Fodinha.Game.Menu;
using Fodinha.Game.UI;
using Fodinha.Hosting;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Fodinha.Game.Tests
{
    public class MenuScreenTests
    {
        private GameObject canvasGo;
        private MenuScreen menu;

        [SetUp]
        public void SetUp()
        {
            canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            menu = UiKit.Rect("Menu", canvasGo.transform).Fill().gameObject.AddComponent<MenuScreen>();
            menu.ShowPage(MenuScreen.Page.Home);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(canvasGo);
        }

        private Button FooterOf(MenuScreen.Page page)
        {
            var pageGo = menu.transform.Find("AreaSegura/Pagina_" + page);
            Assert.That(pageGo, Is.Not.Null, "Página " + page);
            return pageGo.Find("Acao").GetComponent<Button>();
        }

        [Test]
        public void Navegacao_MostraSoAPaginaAtual()
        {
            foreach (MenuScreen.Page page in System.Enum.GetValues(typeof(MenuScreen.Page)))
            {
                menu.ShowPage(page);
                Assert.That(menu.CurrentPage, Is.EqualTo(page));
                var safe = menu.transform.Find("AreaSegura");
                foreach (MenuScreen.Page other in System.Enum.GetValues(typeof(MenuScreen.Page)))
                    Assert.That(safe.Find("Pagina_" + other).gameObject.activeSelf, Is.EqualTo(other == page), other.ToString());
            }
        }

        [Test]
        public void Offline_Comecar_EnviaConfiguracaoComBotsERegras()
        {
            menu.ShowPage(MenuScreen.Page.Offline);
            menu.OfflineRules.PlayersStepper.SetIndex(0);
            menu.OfflineRules.PlayersStepper.Step(+5); // 6 bots

            OfflineMatchConfig received = null;
            menu.OfflineRequested += c => received = c;
            FooterOf(MenuScreen.Page.Offline).onClick.Invoke();

            Assert.That(received, Is.Not.Null);
            Assert.That(received.BotCount, Is.EqualTo(6));
            Assert.That(received.PlayerCount, Is.EqualTo(7));
            Assert.That(received.Rules.IsValid, Is.True);
        }

        [Test]
        public void EditorDeRegras_CarregaEDevolveAsMesmasRegras()
        {
            menu.ShowPage(MenuScreen.Page.Offline);
            var rules = new RuleSet
            {
                StartingLives = 5,
                TurnTimeSeconds = 45,
                ManilhaMode = ManilhaMode.Fixed,
                TieRule = TieRule.SuitBreaksTie,
                LastBidderRestriction = false,
                LifeLossMode = LifeLossMode.Difference,
                Progression = CardProgression.UpAndDown,
                MaxCardsPerPlayer = 7,
                BlindOneCardRounds = false,
                TimeoutsBeforeAutoPlay = 4
            };
            menu.OfflineRules.Load(rules, 9);
            var back = menu.OfflineRules.BuildRules();

            // O editor só muda o que é configurável; as regras fixas voltam sempre como as oficiais.
            Assert.That(JsonUtility.ToJson(back), Is.EqualTo(JsonUtility.ToJson(rules.WithOfficialRules())));
            Assert.That(back.FollowsOfficialRules, Is.True);
            Assert.That(menu.OfflineRules.PlayerOrBotCount, Is.EqualTo(9));
        }

        [Test]
        public void CriarSala_EnviaOpcoes()
        {
            menu.ShowPage(MenuScreen.Page.CreateRoom);
            menu.RoomRules.Load(RuleSet.CreateDefault(), 12, isPublic: false);

            RoomOptions received = null;
            menu.CreateRoomRequested += o => received = o;
            FooterOf(MenuScreen.Page.CreateRoom).onClick.Invoke();

            Assert.That(received, Is.Not.Null);
            Assert.That(received.IsPublic, Is.False);
            Assert.That(received.MaxPlayers, Is.EqualTo(12));
            Assert.That(received.GetValidationErrors(), Is.Empty);
        }

        [TestCase("ab c23-4", "ABC234", true)]
        [TestCase("abc", "ABC", false)]
        [TestCase("ABCDE0", "ABCDE0", false)]
        [TestCase("k7m2qxzz", "K7M2QX", true)]
        public void EntrarComCodigo_NormalizaEValida(string typed, string shown, bool canJoin)
        {
            menu.ShowPage(MenuScreen.Page.JoinRoom);
            menu.CodeField.text = typed;
            Assert.That(menu.CodeField.text, Is.EqualTo(shown));
            Assert.That(menu.JoinButton.interactable, Is.EqualTo(canJoin));

            string requested = null;
            menu.JoinRoomRequested += c => requested = c;
            if (canJoin)
            {
                menu.JoinButton.onClick.Invoke();
                Assert.That(requested, Is.EqualTo(shown));
            }
        }

        [Test]
        public void SalasPublicas_ListaEBotaoEntrar()
        {
            menu.ShowPage(MenuScreen.Page.PublicRooms);
            var rooms = new List<RoomListing>
            {
                new RoomListing { Code = "ABC234", HostName = "Bia", Players = 3, MaxPlayers = 8 },
                new RoomListing { Code = "XYZ789", HostName = "Zé", Players = 8, MaxPlayers = 8 },
                new RoomListing { Code = "QWE456", HostName = "Tião", Players = 4, MaxPlayers = 6, InProgress = true },
            };
            menu.SetPublicRooms(rooms);
            Assert.That(menu.ListedRooms, Is.EqualTo(3));

            var list = menu.transform.Find("AreaSegura/Pagina_PublicRooms/Lista/Conteudo");
            Assert.That(list.childCount, Is.EqualTo(3));
            Assert.That(list.GetChild(0).Find("Entrar").GetComponent<Button>().interactable, Is.True);
            Assert.That(list.GetChild(1).Find("Entrar").GetComponent<Button>().interactable, Is.False, "Sala cheia");
            Assert.That(list.GetChild(2).Find("Entrar").GetComponent<Button>().interactable, Is.False, "Em jogo");

            menu.SetPublicRooms(new List<RoomListing>());
            Assert.That(list.childCount, Is.EqualTo(0));
        }

        [Test]
        public void Moedas_SaldoEBotaoDeGanhar()
        {
            menu.SetCoins(null, false);
            Assert.That(menu.ShownCoins, Is.Null);
            Assert.That(menu.ClaimVisible, Is.False);

            bool claimed = false;
            menu.ClaimRewardRequested += () => claimed = true;
            var claim = menu.transform.Find("AreaSegura/Pagina_Home/Ganhar").GetComponent<Button>();
            Assert.That(UiKit.ButtonLabel(claim).text, Is.EqualTo("+100 moedas"));

            menu.SetCoins(1500, false);
            Assert.That(menu.ShownCoins, Is.EqualTo(1500));
            Assert.That(menu.ClaimVisible, Is.True, "O botão fica sempre à vista na tela inicial.");
            Assert.That(menu.ClaimAvailable, Is.False, "Com 500 ou mais ele fica apagado.");
            claim.onClick.Invoke();
            Assert.That(claimed, Is.False);
            Assert.That(menu.LastToast, Does.Contain("menos de 500"));

            menu.SetCoins(200, true);
            Assert.That(menu.ClaimAvailable, Is.True);
            claim.onClick.Invoke();
            Assert.That(claimed, Is.True);
            Assert.That(MenuScreen.FormatCoins(12345), Is.EqualTo("12.345"));
        }

        [Test]
        public void Ranking_PedeAoAbrir_EMostraLinhas()
        {
            bool asked = false;
            menu.RankingRequested += () => asked = true;
            menu.ShowPage(MenuScreen.Page.Ranking);
            Assert.That(asked, Is.True);

            menu.ShowRanking(new List<MenuScreen.RankingRow>
            {
                new MenuScreen.RankingRow { Position = 1, Name = "Bia", Coins = 900, Wins = 4 },
                new MenuScreen.RankingRow { Position = 2, Name = "Zé", Coins = 600, Wins = 1 }
            }, 2, 600);
            Assert.That(menu.ListedRanking, Is.EqualTo(2));
            var mine = menu.transform.Find("AreaSegura/Pagina_Ranking/Minha").GetComponent<Text>();
            Assert.That(mine.text, Does.Contain("2º").And.Contain("600"));
        }

        [Test]
        public void CriarSala_ComValorDaPartida()
        {
            menu.ShowPage(MenuScreen.Page.CreateRoom);
            menu.RoomRules.Load(RuleSet.CreateDefault(), 4, true, 200);
            RoomOptions received = null;
            menu.CreateRoomRequested += o => received = o;
            FooterOf(MenuScreen.Page.CreateRoom).onClick.Invoke();
            Assert.That(received.Stake, Is.EqualTo(200));

            menu.RoomRules.StakeStepper.SetIndex(0);
            FooterOf(MenuScreen.Page.CreateRoom).onClick.Invoke();
            Assert.That(received.Stake, Is.EqualTo(0), "Grátis.");
        }

        [Test]
        public void CriarSala_PartidaAlta_SoPublica_ComAviso()
        {
            menu.ShowPage(MenuScreen.Page.CreateRoom);
            var rules = menu.RoomRules;
            rules.Load(RuleSet.CreateDefault(), 4, false, 500);
            Assert.That(rules.IsPublic, Is.False);
            Assert.That(rules.HighStakeHintLabel.gameObject.activeSelf, Is.False, "Até 500 não tem aviso.");

            rules.StakeStepper.Step(1); // 1000
            Assert.That(rules.Stake, Is.EqualTo(1000));
            Assert.That(rules.IsPublic, Is.True, "Escolher 1000 deixa a sala pública.");
            Assert.That(rules.HighStakeHintLabel.gameObject.activeSelf, Is.True);

            rules.VisibilityStepper.Step(-1); // privada
            Assert.That(rules.IsPublic, Is.False);
            Assert.That(rules.Stake, Is.EqualTo(500), "Sala privada baixa o valor para 500.");
            Assert.That(rules.HighStakeHintLabel.gameObject.activeSelf, Is.False);

            // Escolha antiga salva no celular (privada valendo 1000) não vale mais.
            rules.Load(RuleSet.CreateDefault(), 4, false, 1000);
            Assert.That(rules.Stake, Is.EqualTo(500));
            RoomOptions received = null;
            menu.CreateRoomRequested += o => received = o;
            FooterOf(MenuScreen.Page.CreateRoom).onClick.Invoke();
            Assert.That(received.IsPublic, Is.False);
            Assert.That(received.Stake, Is.EqualTo(500));
        }

        [Test]
        public void ConvidarAmigosIos_PedeConvite_ComLinkDoSite()
        {
            bool asked = false;
            menu.InviteIosRequested += () => asked = true;
            var button = menu.transform.Find("AreaSegura/Pagina_Home/Botao_Convidar amigos iOS").GetComponent<Button>();
            Assert.That(UiKit.ButtonLabel(button).text, Is.EqualTo("Convidar amigos iOS"));
            button.onClick.Invoke();
            Assert.That(asked, Is.True);

            // A mensagem leva para o site, não para uma sala.
            Assert.That(GamePrefs.IosInviteMessage, Does.Contain(GamePrefs.SiteUrl));
            Assert.That(GamePrefs.IosInviteMessage, Does.Not.Contain("sala="));
            Assert.That(ShareService.Share(GamePrefs.IosInviteMessage), Is.EqualTo(ShareService.Result.Copied), "No Editor, copia.");
            Assert.That(ShareService.LastShared, Is.EqualTo(GamePrefs.IosInviteMessage));
        }

        [Test]
        public void NomeDoJogador_EhLimpo()
        {
            Assert.That(GamePrefs.SanitizeName("  <b>Zé</b>  "), Is.EqualTo("bZé/b"));
            Assert.That(GamePrefs.SanitizeName("NomeMuitoGrandeDemais"), Has.Length.EqualTo(GamePrefs.MaxNameLength));
            Assert.That(GamePrefs.SanitizeName("   "), Is.EqualTo(""));
        }
    }
}
