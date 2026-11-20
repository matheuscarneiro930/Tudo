using System.Collections;
using Fodinha.Game;
using Fodinha.Game.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Fodinha.PlayMode.Tests
{
    /// <summary>Fluxo real: menu → jogar offline com N bots → partida → sair (confirmando) → menu.</summary>
    public class MenuFlowTests
    {
        private static IEnumerator WaitForScene(string name, float timeout = 10f)
        {
            float t = 0f;
            while (SceneManager.GetActiveScene().name != name && t < timeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(name));
        }

        [UnityTest]
        public IEnumerator Menu_JogarOffline_ESairDeVolta()
        {
            SceneManager.LoadScene("Menu");
            yield return WaitForScene("Menu");

            var root = Object.FindAnyObjectByType<MenuRoot>();
            Assert.That(root, Is.Not.Null);
            var menu = root.Screen;
            Assert.That(menu.CurrentPage, Is.EqualTo(MenuScreen.Page.Home));

            menu.ShowPage(MenuScreen.Page.Offline);
            menu.OfflineRules.PlayersStepper.SetIndex(0);
            menu.OfflineRules.PlayersStepper.Step(+7); // 8 bots
            menu.transform.Find("AreaSegura/Pagina_Offline/Acao").GetComponent<Button>().onClick.Invoke();

            yield return WaitForScene("PartidaOffline");
            var game = Object.FindAnyObjectByType<OfflineGameRoot>();
            Assert.That(game, Is.Not.Null);
            Assert.That(game.Controller.View.Players.Count, Is.EqualTo(9), "8 bots + o jogador.");

            // "Sair" no topo pede confirmação; confirmar volta ao menu.
            var exit = game.Screen.transform.Find("AreaSegura/Topo/Sair").GetComponent<Button>();
            Assert.That(exit.gameObject.activeInHierarchy, Is.True);
            exit.onClick.Invoke();
            yield return null;
            var confirm = game.Screen.transform.Find("Dialogo/Caixa/Confirmar");
            Assert.That(confirm, Is.Not.Null, "Deveria abrir a confirmação.");
            confirm.GetComponent<Button>().onClick.Invoke();

            yield return WaitForScene("Menu");
        }

        [UnityTest]
        public IEnumerator Menu_SemOnline_AvisaJogador()
        {
            SceneManager.LoadScene("Menu");
            yield return WaitForScene("Menu");
            var menu = Object.FindAnyObjectByType<MenuRoot>().Screen;

            menu.ShowPage(MenuScreen.Page.JoinRoom);
            menu.CodeField.text = "ABC234";
            menu.JoinButton.onClick.Invoke();
            yield return null;
            Assert.That(menu.LastToast, Does.Contain("online"));
        }
    }
}
