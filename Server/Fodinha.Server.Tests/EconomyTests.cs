using System.Collections.Generic;
using System.Linq;
using Fodinha.Core;
using Fodinha.Hosting;
using Fodinha.Net;
using NUnit.Framework;

namespace Fodinha.Server.Tests
{
    /// <summary>Moedas fictícias: entrada, pote, recompensa, ranking e persistência.</summary>
    public class EconomyTests
    {
        private ServerCore core;

        [SetUp]
        public void SetUp()
        {
            core = new ServerCore(new ServerOptions { Pacing = PacingSettings.Instant }, new SystemRandomSource(11));
        }

        private FakeClient Client(string name) => new FakeClient(core, name).Hello();

        private int Coins(FakeClient c) => core.Accounts.FindByToken(c.Token).Coins;

        private void SetCoins(FakeClient c, int coins)
        {
            var a = core.Accounts.FindByToken(c.Token);
            core.Accounts.AddCoins(a, coins - a.Coins);
        }

        private void PlayUntilOver(IList<FakeClient> clients)
        {
            for (int i = 0; i < 200000; i++)
            {
                if (clients.All(c => c.Room != null && !c.Room.inMatch && c.Match != null)) return;
                foreach (var c in clients) c.AutoAct();
                core.Tick(0.05f);
            }
            Assert.Fail("A partida não terminou.");
        }

        [Test]
        public void ContaNova_Comeca500()
        {
            var c = Client("Ana");
            Assert.That(c.Welcome.coins, Is.EqualTo(500));
            Assert.That(c.Profile.coins, Is.EqualTo(500));
            Assert.That(c.Profile.canClaimReward, Is.False);
        }

        [Test]
        public void SalaValendo_CobraEntrada_VencedorLevaPote()
        {
            var ana = Client("Ana");
            ana.Create(new RoomOptions { MaxPlayers = 3, Stake = 100 });
            var bia = Client("Bia");
            bia.Join(ana.Room.code);
            Assert.That(ana.Room.stake, Is.EqualTo(100));

            ana.Do(Protocol.ToServer.StartMatch);
            Assert.That(Coins(ana), Is.EqualTo(400));
            Assert.That(Coins(bia), Is.EqualTo(400));
            Assert.That(ana.Room.pot, Is.EqualTo(200));
            Assert.That(ana.Profile.delta, Is.EqualTo(-100));
            Assert.That(ana.Profile.reason, Is.EqualTo("entrada"));

            PlayUntilOver(new[] { ana, bia });
            Assert.That(Coins(ana) + Coins(bia), Is.EqualTo(1000), "Sem bots, as moedas só mudam de mão.");
            var winner = Coins(ana) == 600 ? ana : bia;
            Assert.That(Coins(winner), Is.EqualTo(600));
            Assert.That(winner.Profile.reason, Is.EqualTo("vitoria"));
            Assert.That(winner.Profile.wins, Is.EqualTo(1));
            Assert.That(ana.Profile.games, Is.EqualTo(1));
            Assert.That(bia.Profile.games, Is.EqualTo(1));
        }

        [Test]
        public void ComBots_BotNaoColocaMoedas()
        {
            var ana = Client("Ana");
            ana.Create(new RoomOptions { MaxPlayers = 3, Stake = 200 });
            ana.Do(Protocol.ToServer.AddBot);
            ana.Do(Protocol.ToServer.AddBot);
            ana.Do(Protocol.ToServer.StartMatch);
            Assert.That(ana.Room.pot, Is.EqualTo(200), "Só a entrada da Ana.");
            PlayUntilOver(new[] { ana });
            Assert.That(Coins(ana), Is.EqualTo(300).Or.EqualTo(500), "Perde a entrada ou recebe só o que pôs.");
        }

        [Test]
        public void SemSaldo_NaoCriaNemEntra()
        {
            var ana = Client("Ana");
            ana.Create(new RoomOptions { Stake = 1000 });
            Assert.That(ana.Room, Is.Null);
            Assert.That(ana.LastError.code, Is.EqualTo(ErrorCodes.NotEnoughCoins));

            ana.Create(new RoomOptions { Stake = 500 });
            var bia = Client("Bia");
            SetCoins(bia, 300);
            bia.Join(ana.Room.code);
            Assert.That(bia.Room, Is.Null);
            Assert.That(bia.LastError.code, Is.EqualTo(ErrorCodes.NotEnoughCoins));
        }

        [Test]
        public void Iniciar_BloqueiaSeAlguemFicouSemSaldo()
        {
            var ana = Client("Ana");
            ana.Create(new RoomOptions { Stake = 200 });
            var bia = Client("Bia");
            bia.Join(ana.Room.code);
            SetCoins(bia, 100);
            ana.Do(Protocol.ToServer.StartMatch);
            Assert.That(ana.LastError.code, Is.EqualTo(ErrorCodes.NotEnoughCoins));
            Assert.That(ana.Room.inMatch, Is.False);
            Assert.That(Coins(ana), Is.EqualTo(500), "Ninguém paga se a partida não começa.");
        }

        [Test]
        public void PartidaAlta_SalaPrivada_EhRecusada()
        {
            var ana = Client("Ana");
            SetCoins(ana, 3000);
            ana.Create(new RoomOptions { IsPublic = false, Stake = 1000 });
            Assert.That(ana.Room, Is.Null);
            Assert.That(ana.LastError.code, Is.EqualTo(ErrorCodes.InvalidOptions));
            Assert.That(ana.LastError.message, Is.EqualTo(RoomOptions.HighStakeNeedsPublic));

            ana.Create(new RoomOptions { IsPublic = false, Stake = 500 });
            Assert.That(ana.Room, Is.Not.Null, "Privada valendo 500 pode.");
        }

        [Test]
        public void PartidaAlta_NaoViraPrivada()
        {
            var ana = Client("Ana");
            SetCoins(ana, 3000);
            ana.Create(new RoomOptions { IsPublic = true, Stake = 1000 });
            Assert.That(ana.Room.stake, Is.EqualTo(1000));

            ana.Do(Protocol.ToServer.UpdateOptions, new UpdateOptionsMsg { options = new RoomOptions { IsPublic = false, Stake = 1000 } });
            Assert.That(ana.LastError.message, Is.EqualTo(RoomOptions.HighStakeNeedsPublic));
            Assert.That(ana.Room.isPublic, Is.True);

            ana.Do(Protocol.ToServer.UpdateOptions, new UpdateOptionsMsg { options = new RoomOptions { IsPublic = false, Stake = 500 } });
            Assert.That(ana.Room.isPublic, Is.False, "Baixando para 500, pode ser privada.");
            Assert.That(ana.Room.stake, Is.EqualTo(500));
        }

        [Test]
        public void PartidaAlta_SoComBots_NaoComeca_NinguemPaga()
        {
            var ana = Client("Ana");
            SetCoins(ana, 3000);
            ana.Create(new RoomOptions { MaxPlayers = 4, Stake = 1000 });
            ana.Do(Protocol.ToServer.AddBot);
            ana.Do(Protocol.ToServer.AddBot);
            ana.Do(Protocol.ToServer.StartMatch);
            Assert.That(ana.Room.inMatch, Is.False);
            Assert.That(ana.LastError.code, Is.EqualTo(ErrorCodes.NeedPlayers));
            Assert.That(ana.LastError.message, Is.EqualTo(RoomOptions.HighStakeNeedsRival(1000)));
            Assert.That(Coins(ana), Is.EqualTo(3000));
        }

        [Test]
        public void PartidaAlta_ComOutroHumano_Comeca()
        {
            var ana = Client("Ana");
            SetCoins(ana, 3000);
            ana.Create(new RoomOptions { MaxPlayers = 4, Stake = 1000 });
            ana.Do(Protocol.ToServer.AddBot);
            var bia = Client("Bia");
            SetCoins(bia, 1000);
            bia.Join(ana.Room.code);
            ana.Do(Protocol.ToServer.StartMatch);
            Assert.That(ana.Room.inMatch, Is.True);
            Assert.That(ana.Room.pot, Is.EqualTo(2000));
            Assert.That(Coins(ana), Is.EqualTo(2000));
            Assert.That(Coins(bia), Is.EqualTo(0));
        }

        [Test]
        public void Valendo500_ContraBots_ContinuaPodendo()
        {
            var ana = Client("Ana");
            ana.Create(new RoomOptions { IsPublic = false, MaxPlayers = 3, Stake = 500 });
            ana.Do(Protocol.ToServer.AddBot);
            ana.Do(Protocol.ToServer.StartMatch);
            Assert.That(ana.Room.inMatch, Is.True);
            Assert.That(ana.Room.pot, Is.EqualTo(500));
        }

        [Test]
        public void ValorInvalido_EhRecusado()
        {
            var ana = Client("Ana");
            ana.Create(new RoomOptions { Stake = 123 });
            Assert.That(ana.Room, Is.Null);
            Assert.That(ana.LastError.code, Is.EqualTo(ErrorCodes.InvalidOptions));
        }

        [Test]
        public void Recompensa_SoAbaixoDe500_ComIntervalo_EAte500()
        {
            var ana = Client("Ana");
            ana.Do(Protocol.ToServer.ClaimReward);
            Assert.That(ana.LastError.code, Is.EqualTo(ErrorCodes.RewardUnavailable), "Com 500 não ganha.");

            SetCoins(ana, 350);
            ana.Do(Protocol.ToServer.ClaimReward);
            Assert.That(Coins(ana), Is.EqualTo(450));
            Assert.That(ana.Profile.delta, Is.EqualTo(100));
            Assert.That(ana.Profile.reason, Is.EqualTo("recompensa"));

            ana.LastError = null;
            ana.Do(Protocol.ToServer.ClaimReward);
            Assert.That(ana.LastError?.code, Is.EqualTo(ErrorCodes.RewardUnavailable), "Precisa esperar entre um vídeo e outro.");
            Assert.That(Coins(ana), Is.EqualTo(450));

            for (int i = 0; i < 21; i++) core.Tick(1f);
            ana.Do(Protocol.ToServer.ClaimReward);
            Assert.That(Coins(ana), Is.EqualTo(500), "Para em 500.");
            Assert.That(ana.Profile.delta, Is.EqualTo(50));
            Assert.That(ana.Profile.canClaimReward, Is.False);
        }

        [Test]
        public void Ranking_PorMoedas_ComMinhaPosicao()
        {
            var ana = Client("Ana");
            var bia = Client("Bia");
            var caio = Client("Caio");
            SetCoins(ana, 300);
            SetCoins(bia, 900);
            SetCoins(caio, 600);

            ana.Do(Protocol.ToServer.GetRanking);
            var r = ana.LastRanking;
            Assert.That(r.top.Select(e => e.name), Is.EqualTo(new[] { "Bia", "Caio", "Ana" }));
            Assert.That(r.top[0].position, Is.EqualTo(1));
            Assert.That(r.myPosition, Is.EqualTo(3));
            Assert.That(r.myCoins, Is.EqualTo(300));
        }

        [Test]
        public void Persistencia_ServidorReinicia_ContaContinua()
        {
            var store = new MemoryAccountStore();
            var book = new AccountBook { Persist = a => store.SaveAsync(a).Wait() };
            core = new ServerCore(new ServerOptions { Pacing = PacingSettings.Instant }, new SystemRandomSource(3), book);
            var ana = Client("Ana");
            SetCoins(ana, 250);
            core.Accounts.RecordGame(core.Accounts.FindByToken(ana.Token), true);
            string token = ana.Token, id = ana.PlayerId;

            // "Reinicia" o servidor: novo núcleo carregando as contas do banco.
            var reloaded = new AccountBook(store.LoadAllAsync().Result);
            core = new ServerCore(new ServerOptions(), new SystemRandomSource(4), reloaded);
            var volta = new FakeClient(core, "Ana").Hello(token);
            Assert.That(volta.PlayerId, Is.EqualTo(id));
            Assert.That(volta.Token, Is.EqualTo(token));
            Assert.That(volta.Profile.coins, Is.EqualTo(250));
            Assert.That(volta.Profile.wins, Is.EqualTo(1));
        }

        [Test]
        public void TokenNuncaEhGuardadoEmTexto()
        {
            var store = new MemoryAccountStore();
            var book = new AccountBook { Persist = a => store.SaveAsync(a).Wait() };
            core = new ServerCore(new ServerOptions(), new SystemRandomSource(5), book);
            var ana = Client("Ana");
            var saved = store.LoadAllAsync().Result.Single();
            Assert.That(saved.TokenHash, Is.Not.EqualTo(ana.Token));
            Assert.That(saved.TokenHash, Is.EqualTo(AccountBook.HashToken(ana.Token)));
        }

        [Test]
        public void SalaEncerradaNoMeio_DevolveEntrada()
        {
            core = new ServerCore(new ServerOptions { AbandonedMatchSeconds = 30f, MatchReconnectSeconds = 300f }, new SystemRandomSource(6));
            var ana = Client("Ana");
            ana.Create(new RoomOptions { Stake = 100 });
            ana.Do(Protocol.ToServer.AddBot);
            ana.Do(Protocol.ToServer.StartMatch);
            Assert.That(Coins(ana), Is.EqualTo(400));

            core.OnDisconnected(ana);
            for (int i = 0; i < 40; i++) core.Tick(1f);
            Assert.That(core.RoomCount, Is.EqualTo(0));
            Assert.That(Coins(ana), Is.EqualTo(500), "Partida encerrada sem terminar: entrada devolvida.");
        }

        [Test]
        public void SaiuNoMeio_PerdeAEntrada()
        {
            var ana = Client("Ana");
            ana.Create(new RoomOptions { Stake = 100 });
            var bia = Client("Bia");
            bia.Join(ana.Room.code);
            ana.Do(Protocol.ToServer.StartMatch);
            bia.Do(Protocol.ToServer.LeaveRoom);
            PlayUntilOver(new[] { ana });
            Assert.That(Coins(bia), Is.EqualTo(400));
            Assert.That(Coins(ana), Is.EqualTo(600), "A Ana vence (a Bia é eliminada) e leva o pote.");
        }

        [Test]
        public void SalasPublicas_MostramOValor()
        {
            var ana = Client("Ana");
            ana.Create(new RoomOptions { Stake = 50 });
            var bia = Client("Bia");
            bia.Do(Protocol.ToServer.ListRooms);
            Assert.That(bia.LastList.rooms.Single().Stake, Is.EqualTo(50));
        }
    }
}
