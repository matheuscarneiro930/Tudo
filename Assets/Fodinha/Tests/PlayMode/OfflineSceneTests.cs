using System.Collections;
using Fodinha.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fodinha.PlayMode.Tests
{
    /// <summary>Carrega a cena real da partida offline e joga até o fim (com o bot no lugar do jogador).</summary>
    public class OfflineSceneTests
    {
        [UnityTest]
        public IEnumerator CenaOffline_PartidaComBots_RodaAteOFim()
        {
            SceneManager.LoadScene("PartidaOffline");
            yield return null;
            yield return null;

            var root = Object.FindAnyObjectByType<Fodinha.Game.OfflineGameRoot>();
            Assert.That(root, Is.Not.Null, "A cena precisa ter o OfflineGameRoot.");
            var controller = root.Controller;
            Assert.That(controller.IsRunning, Is.True, "A partida deve começar sozinha.");
            Assert.That(root.Screen.CurrentView, Is.Not.Null, "A tela deve estar ligada à sessão.");

            controller.Speed = 200f;
            controller.SetHumanAutoPlay(true);

            float elapsed = 0f;
            while (controller.View.Phase != MatchPhase.MatchOver && elapsed < 180f)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.That(controller.View.Phase, Is.EqualTo(MatchPhase.MatchOver), "A partida não terminou a tempo.");
            yield return null;
            Assert.That(root.Screen.IsMatchOverVisible, Is.True);

            // "Jogar de novo" reinicia.
            controller.Restart();
            yield return null;
            Assert.That(controller.View.RoundNumber, Is.EqualTo(1));
            Assert.That(root.Screen.IsMatchOverVisible, Is.False);
        }
    }
}
