using System.Collections.Generic;
using Fodinha.Core;
using Fodinha.EditorTools;
using Fodinha.Game.UI;
using Fodinha.Hosting;
using NUnit.Framework;
using UnityEngine;

namespace Fodinha.Game.Tests
{
    /// <summary>Mensagens prontas na partida offline, com a interface real.</summary>
    public class QuickChatUiTests
    {
        private readonly List<Object> created = new List<Object>();
        private LocalMatchController controller;
        private MatchScreen screen;
        private bool savedMute;

        [SetUp]
        public void SetUp()
        {
            savedMute = GamePrefs.QuickChatOn;
            GamePrefs.QuickChatOn = true;

            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            created.Add(canvasGo);
            var logic = new GameObject("Partida");
            created.Add(logic);
            controller = logic.AddComponent<LocalMatchController>();
            screen = UiKit.Rect("Tela", canvas.transform).Fill().gameObject.AddComponent<MatchScreen>();
            controller.StartMatch(new OfflineMatchConfig { BotCount = 3, Seed = 11 }, PacingSettings.Instant);
            screen.Bind(controller);
            Assert.That(MatchDriver.AdvanceUntil(controller, v => v.Phase == MatchPhase.Betting, humanActs: false), Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
            GamePrefs.QuickChatOn = savedMute;
        }

        [Test]
        public void Botao_AbreQuadro_ComTodasAsMensagens()
        {
            var chat = screen.Chat;
            Assert.That(chat.OpenButton.gameObject.activeSelf, Is.True);
            Assert.That(chat.IsPanelOpen, Is.False);
            chat.OpenButton.onClick.Invoke();
            Assert.That(chat.IsPanelOpen, Is.True);
            Assert.That(chat.MessageButtons, Has.Count.EqualTo(QuickChat.Count));
            for (int i = 0; i < QuickChat.Count; i++)
                Assert.That(UiKit.ButtonLabel((UnityEngine.UI.Button)chat.MessageButtons[i]).text, Is.EqualTo(QuickChat.Text(i)));
        }

        [Test]
        public void TocarNaMensagem_MostraBalaoEmCimaDoJogador_EFechaQuadro()
        {
            var said = new List<(int, int)>();
            controller.ChatReceived += (s, id) => said.Add((s, id));
            screen.Chat.Open();
            screen.Chat.MessageButtons[QuickChat.Scared].onClick.Invoke();

            Assert.That(said, Is.EqualTo(new List<(int, int)> { (LocalMatchController.HumanSeat, QuickChat.Scared) }));
            Assert.That(screen.Chat.BubbleText(LocalMatchController.HumanSeat), Is.EqualTo("Tá com medo?"));
            Assert.That(screen.Chat.IsPanelOpen, Is.False);
            Assert.That(Audio.Sfx.LastPlayed, Is.EqualTo(Audio.Sound.Chat));
        }

        [Test]
        public void MandarDeNovoNaHora_AvisaParaEsperar()
        {
            int count = 0;
            controller.ChatReceived += (s, id) => count++;
            screen.Chat.Send(QuickChat.Lol);
            screen.Chat.Send(QuickChat.Wow);
            Assert.That(count, Is.EqualTo(1), "Uma mensagem a cada " + QuickChat.MinIntervalSeconds + " s.");
        }

        [Test]
        public void MensagemDoBot_ApareceNoAssentoDele_EDaParaSilenciar()
        {
            screen.Chat.Show(2, QuickChat.Cry);
            Assert.That(screen.Chat.BubbleText(2), Is.EqualTo("Pode chorar!"));
            var mine = screen.Chat.BubblePosition(2);
            screen.Chat.Show(1, QuickChat.Wow);
            Assert.That(screen.Chat.BubblePosition(1), Is.Not.EqualTo(mine), "Cada um fala no próprio lugar.");

            screen.Chat.Open();
            screen.Chat.MuteButton.onClick.Invoke();
            Assert.That(GamePrefs.QuickChatOn, Is.False);
            Assert.That(screen.Chat.BubbleText(2), Is.Null, "Silenciou: some o que estava na tela.");
            screen.Chat.Show(3, QuickChat.Lol);
            Assert.That(screen.Chat.BubbleText(3), Is.Null);

            screen.Chat.Show(LocalMatchController.HumanSeat, QuickChat.GoodPlay);
            Assert.That(screen.Chat.BubbleText(LocalMatchController.HumanSeat), Is.EqualTo("Boa jogada!"), "A própria mensagem sempre aparece.");
        }

        [Test]
        public void BotsRespondem_NaPartidaOffline()
        {
            int fromBots = 0;
            controller.ChatReceived += (s, id) => { if (s != LocalMatchController.HumanSeat) fromBots++; };
            for (int i = 0; i < 15 && fromBots == 0; i++)
            {
                controller.SendChat(QuickChat.Scared);
                for (int t = 0; t < 40; t++) controller.Step(0.1f); // 4 s
            }
            Assert.That(fromBots, Is.GreaterThan(0), "Provocou: algum bot responde (às vezes).");
        }

        [Test]
        public void IconeESom_Existem()
        {
            Assert.That(SpriteFactory.ChatIcon, Is.Not.Null);
            Assert.That(SpriteFactory.InsideChatIcon(0f, 0.25f), Is.True, "Corpo do balão.");
            Assert.That(SpriteFactory.InsideChatIcon(0f, 0.07f), Is.False, "Pontinho vazado.");
            Assert.That(Audio.Sfx.Samples(Audio.Sound.Chat).Length, Is.GreaterThan(1000));
        }
    }
}
