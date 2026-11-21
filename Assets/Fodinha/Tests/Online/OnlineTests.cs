using System.Collections.Generic;
using Fodinha.Core;
using Fodinha.Game.UI;
using Fodinha.Hosting;
using Fodinha.Net;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace Fodinha.Online.Tests
{
    public class ViewMapperTests
    {
        /// <summary>
        /// Para cada estado de várias partidas e cada assento: visão → rede (JSON) → visão → rede deve dar o mesmo JSON.
        /// Garante que nada se perde (nem aparece) no caminho servidor → cliente.
        /// </summary>
        [TestCase(2, 1)]
        [TestCase(4, 2)]
        [TestCase(9, 3)]
        [TestCase(15, 4)]
        public void IdaEVolta_PeloJson_PreservaAVisao(int players, int seed)
        {
            var names = new List<string>();
            for (int i = 0; i < players; i++) names.Add("J" + i);
            var engine = new MatchEngine(RuleSet.CreateDefault(), names, new SystemRandomSource(seed));
            var director = new MatchDirector(engine, PacingSettings.Instant, new SystemRandomSource(seed + 1));
            for (int s = 0; s < players; s++) director.SetBotSeat(s, true);
            director.Start();

            int checkedStates = 0;
            var phasesSeen = new HashSet<MatchPhase>();
            for (int step = 0; step < 100000 && engine.Phase != MatchPhase.MatchOver; step++)
            {
                if (step % 3 == 0)
                {
                    for (int seat = 0; seat < players; seat++)
                    {
                        var original = ViewMapper.ToDto(engine.BuildView(seat));
                        string json = Protocol.Encode(Protocol.ToClient.Match, new MatchUpdateDto { view = original });
                        Assert.That(Protocol.TryDecode(json, out var env), Is.True);
                        var received = Protocol.Payload<MatchUpdateDto>(env).view;
                        var rebuilt = ViewMapper.ToDto(ViewMapper.ToView(received));
                        Assert.That(JsonConvert.SerializeObject(rebuilt), Is.EqualTo(JsonConvert.SerializeObject(original)));
                    }
                    checkedStates++;
                    phasesSeen.Add(engine.Phase);
                }
                director.Tick(0.02f);
            }
            Assert.That(checkedStates, Is.GreaterThan(10));
            Assert.That(phasesSeen, Is.SupersetOf(new[] { MatchPhase.Betting, MatchPhase.Playing }));
        }

        [Test]
        public void VisaoReconstruida_TemComparadorEPermiteDesenhar()
        {
            var engine = new MatchEngine(RuleSet.CreateDefault(), new[] { "A", "B", "C" }, new SystemRandomSource(3));
            engine.StartMatch(0);
            var view = ViewMapper.ToView(ViewMapper.ToDto(engine.BuildView(1)));
            Assert.That(view.Comparer, Is.Not.Null);
            Assert.That(view.Vira, Is.EqualTo(engine.Vira));
            Assert.That(view.ViewerSeat, Is.EqualTo(1));
            Assert.That(view.Players.Count, Is.EqualTo(3));
        }
    }

    public class NetworkMatchSessionTests
    {
        private sealed class FakeSender : IOnlineSender
        {
            public readonly List<string> Sent = new List<string>();
            public void Send(string type, object payload = null) => Sent.Add(Protocol.Encode(type, payload));
        }

        private static MatchUpdateDto Update(int seat, out MatchEngine engine)
        {
            engine = new MatchEngine(RuleSet.CreateDefault(), new[] { "A", "B" }, new SystemRandomSource(7));
            engine.StartMatch(1);
            return new MatchUpdateDto
            {
                view = ViewMapper.ToDto(engine.BuildView(seat)),
                timerSeat = 0,
                turnRemaining = 20f,
                turnTotal = 30f,
                events = new List<EventDto> { new EventDto { type = (int)MatchEventType.RoundStarted, seat = 1, value = 1 } }
            };
        }

        [Test]
        public void Atualizacao_GeraVisaoEventosERelogio()
        {
            float now = 100f;
            var session = new NetworkMatchSession(new FakeSender(), () => now);
            MatchView received = null;
            var events = new List<MatchEvent>();
            session.ViewChanged += v => received = v;
            session.EventReceived += events.Add;

            session.Apply(Update(0, out _));
            Assert.That(received, Is.Not.Null);
            Assert.That(session.LocalSeat, Is.EqualTo(0));
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(session.TurnTimeRemaining, Is.EqualTo(20f).Within(0.01f));
            now += 5f;
            Assert.That(session.TurnTimeRemaining, Is.EqualTo(15f).Within(0.01f));
            now += 60f;
            Assert.That(session.TurnTimeRemaining, Is.EqualTo(0f));
        }

        [Test]
        public void Pedidos_ViramMensagens_ERecusaViraAviso()
        {
            var sender = new FakeSender();
            var session = new NetworkMatchSession(sender);
            session.PlaceBet(1);
            session.PlayCard(Card.Parse("7C"));
            session.ResumeControl();
            Assert.That(sender.Sent[0], Does.Contain("\"t\":\"bet\"").And.Contain("\"value\":1"));
            Assert.That(sender.Sent[1], Does.Contain("\"t\":\"play\"").And.Contain("\"card\":" + Card.Parse("7C").Id));
            Assert.That(sender.Sent[2], Does.Contain("\"t\":\"resume\""));

            ActionResult? rejected = null;
            session.ActionRejected += r => rejected = r;
            session.ApplyError(new ErrorMsg { code = ErrorCodes.Action, action = (int)ActionResult.NotYourTurn });
            Assert.That(rejected, Is.EqualTo(ActionResult.NotYourTurn));
        }

        [Test]
        public void TelaDaPartida_DesenhaVisaoVindaDaRede()
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            try
            {
                canvasGo.AddComponent<Canvas>();
                var screen = UiKit.Rect("Tela", canvasGo.transform).Fill().gameObject.AddComponent<MatchScreen>();
                var sender = new FakeSender();
                var session = new NetworkMatchSession(sender);
                screen.Bind(session);
                session.Apply(Update(0, out var engine));
                Assert.That(screen.CurrentView, Is.Not.Null);
                Assert.That(screen.CurrentView.Phase, Is.EqualTo(MatchPhase.Betting));
                Assert.That(screen.IsBetPanelVisible, Is.EqualTo(engine.CurrentSeat == 0));
            }
            finally
            {
                Object.DestroyImmediate(canvasGo);
            }
        }
    }

    public class OnlineClientTests
    {
        private GameObject go;
        private OnlineClient client;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("ClienteTeste");
            client = go.AddComponent<OnlineClient>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(go);

        [Test]
        public void Mensagens_AtualizamEstadoEDisparamEventos()
        {
            client.Dispatch(Protocol.Encode(Protocol.ToClient.Welcome, new WelcomeMsg { playerId = "p1", token = "tok123" }));
            Assert.That(client.PlayerId, Is.EqualTo("p1"));
            Assert.That(Fodinha.Game.GamePrefs.OnlineToken, Is.EqualTo("tok123"));

            RoomStateDto room = null;
            client.RoomStateReceived += r => room = r;
            client.Dispatch(Protocol.Encode(Protocol.ToClient.RoomState, new RoomStateDto { code = "ABC234", you = "p1", ownerId = "p1" }));
            Assert.That(room.IAmOwner, Is.True);
            Assert.That(client.Room.code, Is.EqualTo("ABC234"));

            string left = null;
            client.LeftRoom += r => left = r;
            client.Dispatch(Protocol.Encode(Protocol.ToClient.LeftRoom, new LeftRoomMsg { reason = "abandonada" }));
            Assert.That(left, Is.EqualTo("abandonada"));
            Assert.That(client.Room, Is.Null);

            client.Dispatch("lixo que não é json");
        }

        [Test]
        public void Perfil_AtualizaSaldo_ERanking()
        {
            ProfileMsg got = null;
            client.ProfileReceived += p => got = p;
            client.Dispatch(Protocol.Encode(Protocol.ToClient.Profile, new ProfileMsg { coins = 350, delta = 100, reason = "recompensa", canClaimReward = true }));
            Assert.That(got.coins, Is.EqualTo(350));
            Assert.That(client.Profile.canClaimReward, Is.True);

            RankingMsg rank = null;
            client.RankingReceived += r => rank = r;
            var msg = new RankingMsg { myPosition = 3, myCoins = 350 };
            msg.top.Add(new RankEntry { position = 1, name = "Bia", coins = 900 });
            client.Dispatch(Protocol.Encode(Protocol.ToClient.Ranking, msg));
            Assert.That(rank.top[0].name, Is.EqualTo("Bia"));
            Assert.That(rank.myPosition, Is.EqualTo(3));
        }

        [TestCase("wss://fodinha-server-0ccs.onrender.com/ws", "https://fodinha-server-0ccs.onrender.com/health")]
        [TestCase("ws://192.168.0.10:5080/ws", "http://192.168.0.10:5080/health")]
        [TestCase("nao e url", null)]
        [TestCase("https://x.com/ws", null)]
        public void EnderecoDoHealth(string server, string expected)
        {
            Assert.That(OnlineClient.HealthUrl(server), Is.EqualTo(expected));
        }

        [Test]
        public void ErroDeAcao_TemTextoAmigavel()
        {
            Assert.That(OnlineClient.Describe(new ErrorMsg { code = ErrorCodes.Action, action = (int)ActionResult.NotYourTurn }),
                        Is.EqualTo("Ainda não é a sua vez"));
            Assert.That(OnlineClient.Describe(new ErrorMsg { code = ErrorCodes.RoomFull, message = "A sala está cheia." }),
                        Is.EqualTo("A sala está cheia."));
        }
    }

    public class LobbyScreenTests
    {
        private GameObject canvasGo;
        private LobbyScreen lobby;

        [SetUp]
        public void SetUp()
        {
            canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.AddComponent<Canvas>();
            lobby = UiKit.Rect("Sala", canvasGo.transform).Fill().gameObject.AddComponent<LobbyScreen>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(canvasGo);

        private static RoomStateDto Room(string you) => new RoomStateDto
        {
            code = "K7M2QX", isPublic = true, maxPlayers = 4, rules = RuleSet.CreateDefault(), ownerId = "dono", you = you,
            members = new List<MemberDto>
            {
                new MemberDto { id = "dono", name = "Dono", owner = true, connected = true },
                new MemberDto { id = "bot1", name = "Zé", bot = true, connected = true },
                new MemberDto { id = "bia", name = "Bia", connected = false }
            }
        };

        [Test]
        public void Dono_VeControles_Convidado_Nao()
        {
            lobby.Render(Room("dono"));
            Assert.That(lobby.StartButton.gameObject.activeSelf, Is.True);
            Assert.That(lobby.StartButton.interactable, Is.True);
            Assert.That(lobby.BotButton.gameObject.activeSelf, Is.True);
            Assert.That(lobby.ListedPlayers, Is.EqualTo(3));

            lobby.Render(Room("bia"));
            Assert.That(lobby.StartButton.gameObject.activeSelf, Is.False);
            Assert.That(lobby.BotButton.gameObject.activeSelf, Is.False);
            Assert.That(lobby.RulesButton.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void Dono_RemoveBot_EAlteraRegras()
        {
            lobby.Render(Room("dono"));
            int removed = -1;
            lobby.RemoveBotRequested += i => removed = i;
            var row = lobby.transform.Find("AreaSegura/Jogadores/Conteudo/Jogador_1");
            row.Find("Remover").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            Assert.That(removed, Is.EqualTo(1));

            RoomOptions submitted = null;
            lobby.OptionsSubmitted += o => submitted = o;
            lobby.OpenRules();
            Assert.That(lobby.IsRulesOpen, Is.True);
            lobby.SubmitRules();
            Assert.That(submitted, Is.Not.Null);
            Assert.That(submitted.MaxPlayers, Is.EqualTo(4));
            Assert.That(lobby.IsRulesOpen, Is.False);
        }

        [Test]
        public void ResumoDasRegras()
        {
            var text = LobbyScreen.RulesSummary(RuleSet.CreateDefault());
            Assert.That(text, Does.Contain("3 vidas").And.Contain("30 s para apostar").And.Contain("mão cega"));
        }
    }
}
