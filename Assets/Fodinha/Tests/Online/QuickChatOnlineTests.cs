using System.Collections.Generic;
using Fodinha.Core;
using Fodinha.Game;
using Fodinha.Game.UI;
using Fodinha.Hosting;
using Fodinha.Net;
using NUnit.Framework;
using UnityEngine;

namespace Fodinha.Online.Tests
{
    /// <summary>Mensagens prontas online: vai só o número; o que chega do servidor vira balão.</summary>
    public class QuickChatOnlineTests
    {
        private sealed class FakeSender : IOnlineSender
        {
            public readonly List<string> Sent = new List<string>();
            public void Send(string type, object payload = null) => Sent.Add(Protocol.Encode(type, payload));
        }

        [Test]
        public void Enviar_MandaSoONumero()
        {
            var sender = new FakeSender();
            var session = new NetworkMatchSession(sender);
            session.SendChat(QuickChat.Cry);
            session.SendChat(-5);
            session.SendChat(999);
            Assert.That(sender.Sent, Has.Count.EqualTo(1));
            Assert.That(sender.Sent[0], Does.Contain("\"t\":\"chat\"").And.Contain("\"id\":" + QuickChat.Cry));
            Assert.That(sender.Sent[0], Does.Not.Contain("chorar"));
        }

        [Test]
        public void Receber_ViraEvento_InvalidoEhIgnorado()
        {
            var session = new NetworkMatchSession(new FakeSender());
            var got = new List<(int, int)>();
            session.ChatReceived += (s, id) => got.Add((s, id));
            session.ApplyChat(new ChatDto { seat = 1, id = QuickChat.GoodPlay });
            session.ApplyChat(new ChatDto { seat = 1, id = 77 });
            session.ApplyChat(new ChatDto { seat = -1, id = 0 });
            session.ApplyChat(null);
            Assert.That(got, Is.EqualTo(new List<(int, int)> { (1, QuickChat.GoodPlay) }));
        }

        [Test]
        public void Cliente_RepassaMensagemDoServidor()
        {
            var go = new GameObject("ClienteTeste");
            try
            {
                var client = go.AddComponent<OnlineClient>();
                ChatDto got = null;
                client.ChatReceived += c => got = c;
                client.Dispatch(Protocol.Encode(Protocol.ToClient.Chat, new ChatDto { seat = 2, id = QuickChat.Lol }));
                Assert.That(got.seat, Is.EqualTo(2));
                Assert.That(got.id, Is.EqualTo(QuickChat.Lol));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TelaOnline_BotaoMandaPeloServidor_EBalaoAparece()
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            try
            {
                GamePrefs.QuickChatOn = true;
                canvasGo.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var screen = UiKit.Rect("Tela", canvasGo.transform).Fill().gameObject.AddComponent<MatchScreen>();
                var sender = new FakeSender();
                var session = new NetworkMatchSession(sender);
                screen.Bind(session);
                var engine = new MatchEngine(RuleSet.CreateDefault(), new[] { "Eu", "Bia" }, new SystemRandomSource(3));
                engine.StartMatch(1);
                session.Apply(new MatchUpdateDto { view = ViewMapper.ToDto(engine.BuildView(0)) });

                Assert.That(screen.Chat.OpenButton.gameObject.activeSelf, Is.True);
                screen.Chat.OpenButton.onClick.Invoke();
                screen.Chat.MessageButtons[QuickChat.GoodPlay].onClick.Invoke();
                Assert.That(sender.Sent.FindAll(s => s.Contains("\"t\":\"chat\"")), Has.Count.EqualTo(1));
                Assert.That(screen.Chat.BubbleText(0), Is.Null, "O balão só aparece quando o servidor confirma (todos veem juntos).");

                session.ApplyChat(new ChatDto { seat = 0, id = QuickChat.GoodPlay });
                session.ApplyChat(new ChatDto { seat = 1, id = QuickChat.Scared });
                Assert.That(screen.Chat.BubbleText(0), Is.EqualTo("Boa jogada!"));
                Assert.That(screen.Chat.BubbleText(1), Is.EqualTo("Tá com medo?"));
            }
            finally
            {
                Object.DestroyImmediate(canvasGo);
            }
        }

        [TestCase(RuntimePlatform.Android, "", "android")]
        [TestCase(RuntimePlatform.WebGLPlayer, "iPhone OS 17_1", "site-iphone")]
        [TestCase(RuntimePlatform.WebGLPlayer, "iPad OS 16", "site-iphone")]
        [TestCase(RuntimePlatform.WebGLPlayer, "Android 14", "site-android")]
        [TestCase(RuntimePlatform.WebGLPlayer, "Windows 10", "site-pc")]
        [TestCase(RuntimePlatform.WindowsEditor, "Windows 10", "pc")]
        public void Plataforma_ParaOsNumerosDoPainel(RuntimePlatform platform, string os, string expected)
        {
            Assert.That(GamePrefs.PlatformTagFor(platform, os), Is.EqualTo(expected));
        }

        [Test]
        public void Convite_LevaAoSite_ComMarcaDeConvite()
        {
            Assert.That(GamePrefs.InviteUrl, Does.StartWith(GamePrefs.SiteUrl).And.Contain("origem=convite"));
            Assert.That(GamePrefs.IosInviteMessage, Does.Contain(GamePrefs.InviteUrl));
        }
    }
}
