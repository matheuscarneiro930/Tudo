using System.Collections.Generic;
using System.Linq;
using Fodinha.Core;
using Fodinha.Hosting;
using Fodinha.Net;
using NUnit.Framework;

namespace Fodinha.Server.Tests
{
    public class ServerCoreTests
    {
        private ServerCore core;

        [SetUp]
        public void SetUp()
        {
            core = new ServerCore(new ServerOptions { Pacing = PacingSettings.Instant }, new SystemRandomSource(42));
        }

        private FakeClient Client(string name) => new FakeClient(core, name).Hello();

        private (FakeClient owner, string code) OwnerWithRoom(RoomOptions options = null)
        {
            var owner = Client("Dono");
            owner.Create(options);
            Assert.That(owner.Room, Is.Not.Null, owner.LastError?.message);
            return (owner, owner.Room.code);
        }

        /// <summary>Roda o servidor até a partida acabar, com os clientes jogando automaticamente.</summary>
        private void PlayUntilOver(IList<FakeClient> clients, int maxTicks = 200000)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                bool over = clients.Where(c => c.Match != null).All(c => c.Match.view.phase == (int)MatchPhase.MatchOver);
                if (over && clients.Any(c => c.Match != null)) return;
                foreach (var c in clients) c.AutoAct();
                core.Tick(0.05f);
            }
            Assert.Fail("A partida não terminou.");
        }

        [Test]
        public void Hello_RecebeIdEToken()
        {
            var c = Client("Ana");
            Assert.That(c.PlayerId, Is.Not.Null.And.Not.Empty);
            Assert.That(c.Token, Is.Not.Null.And.Length.GreaterThan(20));
        }

        [Test]
        public void VersaoDiferente_EhRecusada()
        {
            var c = new FakeClient(core, "Velho");
            c.Do(Protocol.ToServer.Hello, new HelloMsg { name = "Velho", version = Protocol.Version + 1 });
            Assert.That(c.LastError.code, Is.EqualTo(ErrorCodes.Version));
            Assert.That(c.PlayerId, Is.Null);
        }

        [Test]
        public void SemHello_PedidoEhRecusado()
        {
            var c = new FakeClient(core, "X");
            c.Do(Protocol.ToServer.ListRooms);
            Assert.That(c.LastError.code, Is.EqualTo(ErrorCodes.NotHello));
        }

        [Test]
        public void MensagemInvalida_NaoDerrubaServidor()
        {
            var c = Client("X");
            core.OnMessage(c, "{isso nao e json");
            Assert.That(c.LastError.code, Is.EqualTo(ErrorCodes.BadMessage));
            c.Do(Protocol.ToServer.Bet, null);
            Assert.That(c.LastError.code, Is.EqualTo(ErrorCodes.NotInMatch));
        }

        [Test]
        public void CriarSala_DonoComCodigoValido()
        {
            var (owner, code) = OwnerWithRoom();
            Assert.That(RoomCode.IsValid(code), Is.True);
            Assert.That(owner.Room.ownerId, Is.EqualTo(owner.PlayerId));
            Assert.That(owner.Room.IAmOwner, Is.True);
            Assert.That(owner.Room.members.Count, Is.EqualTo(1));
        }

        [Test]
        public void CriarSala_OpcoesInvalidas_SaoRecusadas()
        {
            var c = Client("X");
            var bad = new RoomOptions { MaxPlayers = 30 };
            c.Create(bad);
            Assert.That(c.Room, Is.Null);
            Assert.That(c.LastError.code, Is.EqualTo(ErrorCodes.InvalidOptions));
        }

        [Test]
        public void Sala_SempreUsaRegrasOficiais()
        {
            var c = Client("X");
            var opts = new RoomOptions();
            opts.Rules.TieRule = TieRule.CancelOut;
            opts.Rules.LifeLossMode = LifeLossMode.Difference;
            opts.Rules.BlindOneCardRounds = false;
            opts.Rules.StartingLives = 4;
            c.Create(opts);
            Assert.That(c.Room.rules.FollowsOfficialRules, Is.True);
            Assert.That(c.Room.rules.StartingLives, Is.EqualTo(4), "O que é configurável é mantido.");

            opts.Rules.ManilhaMode = ManilhaMode.Fixed;
            c.Do(Protocol.ToServer.UpdateOptions, new UpdateOptionsMsg { options = opts });
            Assert.That(c.Room.rules.ManilhaMode, Is.EqualTo(ManilhaMode.Vira));
        }

        [Test]
        public void EntrarComCodigo_TodosVeemOsMembros()
        {
            var (owner, code) = OwnerWithRoom();
            var bia = Client("Bia");
            bia.Join(code.ToLowerInvariant());
            Assert.That(bia.Room.code, Is.EqualTo(code));
            Assert.That(owner.Room.members.Select(m => m.name), Is.EquivalentTo(new[] { "Dono", "Bia" }));
            Assert.That(bia.Room.IAmOwner, Is.False);
        }

        [Test]
        public void CodigoErrado_SalaCheia()
        {
            var c = Client("X");
            c.Join("ZZZZZZ");
            Assert.That(c.LastError.code, Is.EqualTo(ErrorCodes.RoomNotFound));

            var (owner, code) = OwnerWithRoom(new RoomOptions { MaxPlayers = 2 });
            Client("Bia").Join(code);
            c.Join(code);
            Assert.That(c.LastError.code, Is.EqualTo(ErrorCodes.RoomFull));
        }

        [Test]
        public void SalasPublicas_ListaSoAsPublicas()
        {
            OwnerWithRoom(new RoomOptions { IsPublic = true });
            var priv = Client("Priv");
            priv.Create(new RoomOptions { IsPublic = false });

            var c = Client("Olhando");
            c.Do(Protocol.ToServer.ListRooms);
            Assert.That(c.LastList.rooms.Count, Is.EqualTo(1));
            Assert.That(c.LastList.rooms[0].HostName, Is.EqualTo("Dono"));
            Assert.That(c.LastList.rooms[0].Players, Is.EqualTo(1));
        }

        [Test]
        public void SoODonoMexe_EPrecisaDeDoisJogadores()
        {
            var (owner, code) = OwnerWithRoom();
            owner.Do(Protocol.ToServer.StartMatch);
            Assert.That(owner.LastError.code, Is.EqualTo(ErrorCodes.NeedPlayers));

            var bia = Client("Bia");
            bia.Join(code);
            bia.Do(Protocol.ToServer.StartMatch);
            Assert.That(bia.LastError.code, Is.EqualTo(ErrorCodes.NotOwner));
            bia.Do(Protocol.ToServer.AddBot);
            Assert.That(bia.LastError.code, Is.EqualTo(ErrorCodes.NotOwner));
            bia.Do(Protocol.ToServer.UpdateOptions, new UpdateOptionsMsg { options = new RoomOptions() });
            Assert.That(bia.LastError.code, Is.EqualTo(ErrorCodes.NotOwner));
        }

        [Test]
        public void Bots_AdicionarERemover()
        {
            var (owner, _) = OwnerWithRoom(new RoomOptions { MaxPlayers = 3 });
            owner.Do(Protocol.ToServer.AddBot);
            owner.Do(Protocol.ToServer.AddBot);
            Assert.That(owner.Room.members.Count(m => m.bot), Is.EqualTo(2));
            owner.Do(Protocol.ToServer.AddBot);
            Assert.That(owner.LastError.code, Is.EqualTo(ErrorCodes.RoomFull));

            owner.Do(Protocol.ToServer.RemoveBot, new RemoveBotMsg { index = 0 });
            Assert.That(owner.LastError.message, Does.Contain("não é um bot"));
            owner.Do(Protocol.ToServer.RemoveBot, new RemoveBotMsg { index = 1 });
            Assert.That(owner.Room.members.Count, Is.EqualTo(2));
        }

        [Test]
        public void AlterarOpcoes_NaoPodeFicarMenorQueOsJogadores()
        {
            var (owner, code) = OwnerWithRoom();
            Client("Bia").Join(code);
            Client("Caio").Join(code);
            owner.Do(Protocol.ToServer.UpdateOptions, new UpdateOptionsMsg { options = new RoomOptions { MaxPlayers = 2 } });
            Assert.That(owner.LastError.code, Is.EqualTo(ErrorCodes.InvalidOptions));

            var opts = new RoomOptions { MaxPlayers = 10, IsPublic = false };
            opts.Rules.StartingLives = 5;
            owner.Do(Protocol.ToServer.UpdateOptions, new UpdateOptionsMsg { options = opts });
            Assert.That(owner.Room.maxPlayers, Is.EqualTo(10));
            Assert.That(owner.Room.rules.StartingLives, Is.EqualTo(5));
            Assert.That(owner.Room.isPublic, Is.False);
        }

        [Test]
        public void DonoSaiDoLobby_DonoPassaParaOProximo()
        {
            var (owner, code) = OwnerWithRoom();
            var bia = Client("Bia");
            bia.Join(code);
            owner.Do(Protocol.ToServer.LeaveRoom);
            Assert.That(owner.LastLeft, Is.Not.Null);
            Assert.That(bia.Room.IAmOwner, Is.True);
            Assert.That(bia.Room.members.Count, Is.EqualTo(1));
        }

        [Test]
        public void UltimoHumanoSai_SalaApagada()
        {
            var (owner, _) = OwnerWithRoom();
            owner.Do(Protocol.ToServer.AddBot);
            owner.Do(Protocol.ToServer.LeaveRoom);
            Assert.That(core.RoomCount, Is.EqualTo(0));
        }

        [TestCase(2, 0)]
        [TestCase(3, 2)]
        [TestCase(5, 10)]
        public void PartidaCompleta_ViaServidor(int humans, int bots)
        {
            var (owner, code) = OwnerWithRoom(new RoomOptions { MaxPlayers = humans + bots });
            var clients = new List<FakeClient> { owner };
            for (int i = 1; i < humans; i++)
            {
                var c = Client("H" + i);
                c.Join(code);
                clients.Add(c);
            }
            for (int i = 0; i < bots; i++) owner.Do(Protocol.ToServer.AddBot);

            owner.Do(Protocol.ToServer.StartMatch);
            foreach (var c in clients)
            {
                Assert.That(c.Room.inMatch, Is.True);
                Assert.That(c.Match, Is.Not.Null);
            }
            Assert.That(clients.Select(c => c.MySeat).Distinct().Count(), Is.EqualTo(humans), "Cada um num assento.");

            PlayUntilOver(clients);
            foreach (var c in clients)
            {
                Assert.That(c.Match.view.phase, Is.EqualTo((int)MatchPhase.MatchOver));
                Assert.That(c.Match.view.winner, Is.InRange(0, humans + bots - 1));
                Assert.That(c.Room.inMatch, Is.False, "Depois do fim, a sala volta para o lobby (revanche).");
            }
            Assert.That(owner.Room.members.Count, Is.EqualTo(humans + bots));
        }

        [Test]
        public void JogadaInvalida_EhRecusadaComMotivo()
        {
            var (owner, code) = OwnerWithRoom();
            var bia = Client("Bia");
            bia.Join(code);
            owner.Do(Protocol.ToServer.StartMatch);

            var notMyTurn = owner.Match.view.current == owner.MySeat ? bia : owner;
            notMyTurn.Do(Protocol.ToServer.Bet, new BetMsg { value = 0 });
            Assert.That(notMyTurn.LastError.code, Is.EqualTo(ErrorCodes.Action));
            Assert.That((ActionResult)notMyTurn.LastError.action, Is.EqualTo(ActionResult.NotYourTurn));

            notMyTurn.Do(Protocol.ToServer.Play, new PlayMsg { card = 99 });
            Assert.That((ActionResult)notMyTurn.LastError.action, Is.EqualTo(ActionResult.CardNotInHand));
        }

        [Test]
        public void Desconectou_BotJoga_Reconectou_VoltaAoLugar()
        {
            var (owner, code) = OwnerWithRoom();
            var bia = Client("Bia");
            bia.Join(code);
            owner.Do(Protocol.ToServer.AddBot);
            owner.Do(Protocol.ToServer.StartMatch);
            int biaSeat = bia.MySeat;

            core.OnDisconnected(bia);
            Assert.That(owner.Match.view.players[biaSeat].connected, Is.False);

            // A partida segue sem a Bia (o bot joga por ela).
            for (int i = 0; i < 2000 && owner.Match.view.round < 2; i++) { owner.AutoAct(); core.Tick(0.05f); }
            Assert.That(owner.Match.view.round, Is.EqualTo(2));
            Assert.That(owner.Match.view.phase, Is.Not.EqualTo((int)MatchPhase.MatchOver));

            var biaVolta = new FakeClient(core, "Bia").Hello(bia.Token);
            Assert.That(biaVolta.PlayerId, Is.EqualTo(bia.PlayerId));
            Assert.That(biaVolta.Room?.inMatch, Is.True);
            Assert.That(biaVolta.Match, Is.Not.Null);
            Assert.That(biaVolta.MySeat, Is.EqualTo(biaSeat));
            Assert.That(owner.Match.view.players[biaSeat].connected, Is.True);
        }

        [Test]
        public void MesmoTokenEmOutraConexao_DerrubaAAntiga()
        {
            var a = Client("Ana");
            var b = new FakeClient(core, "Ana").Hello(a.Token);
            Assert.That(a.Closed, Is.True);
            Assert.That(b.PlayerId, Is.EqualTo(a.PlayerId));
        }

        [Test]
        public void SaiuNoMeio_EhEliminadoNoFimDaRodada()
        {
            var (owner, code) = OwnerWithRoom();
            var bia = Client("Bia");
            bia.Join(code);
            owner.Do(Protocol.ToServer.AddBot);
            owner.Do(Protocol.ToServer.StartMatch);
            int biaSeat = bia.MySeat;

            bia.Do(Protocol.ToServer.LeaveRoom);
            Assert.That(bia.LastLeft, Is.Not.Null);

            for (int i = 0; i < 2000 && owner.Match.view.phase != (int)MatchPhase.RoundComplete && owner.Match.view.phase != (int)MatchPhase.MatchOver; i++)
            {
                owner.AutoAct();
                core.Tick(0.05f);
            }
            Assert.That(owner.Match.view.players[biaSeat].elim, Is.True);
        }

        [Test]
        public void DesconectadoNoLobby_PerdeOLugarDepoisDoPrazo()
        {
            var (owner, code) = OwnerWithRoom();
            var bia = Client("Bia");
            bia.Join(code);
            core.OnDisconnected(bia);
            Assert.That(owner.Room.members.Single(m => m.name == "Bia").connected, Is.False);

            for (int i = 0; i < 30; i++) core.Tick(1f);
            Assert.That(owner.Room.members.Count, Is.EqualTo(2), "Ainda dentro do prazo.");
            for (int i = 0; i < 40; i++) core.Tick(1f);
            core.OnMessage(owner, Protocol.Encode(Protocol.ToServer.JoinRoom, new JoinRoomMsg { code = code }));
            Assert.That(owner.Room.members.Count, Is.EqualTo(1));
        }

        [Test]
        public void PartidaAbandonada_EhEncerrada()
        {
            var (owner, _) = OwnerWithRoom();
            owner.Do(Protocol.ToServer.AddBot);
            owner.Do(Protocol.ToServer.StartMatch);
            core.OnDisconnected(owner);
            for (int i = 0; i < 400; i++) core.Tick(1f);
            Assert.That(core.RoomCount, Is.EqualTo(0));
        }

        [Test]
        public void NomeEhLimpo()
        {
            Assert.That(ServerCore.SanitizeName("  <b>Zé</b>  "), Is.EqualTo("bZé/b"));
            Assert.That(ServerCore.SanitizeName(new string('x', 40)).Length, Is.EqualTo(14));
            Assert.That(ServerCore.SanitizeName(""), Is.EqualTo("Jogador"));
        }
    }
}
