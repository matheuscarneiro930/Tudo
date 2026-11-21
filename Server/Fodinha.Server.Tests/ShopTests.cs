using System.Collections.Generic;
using System.Linq;
using Fodinha.Core;
using Fodinha.Hosting;
using Fodinha.Net;
using NUnit.Framework;

namespace Fodinha.Server.Tests
{
    /// <summary>Avatar, loja de mesas/baralhos e código VIP (sem anúncios).</summary>
    public class ShopTests
    {
        private ServerCore core;
        private const string Vip = "SEGREDO-123";

        [SetUp]
        public void SetUp()
        {
            core = new ServerCore(new ServerOptions { Pacing = PacingSettings.Instant, VipCode = Vip }, new SystemRandomSource(5));
        }

        private FakeClient Client(string name, string avatar = null) => new FakeClient(core, name).Hello(null, avatar);

        private Account AccountOf(FakeClient c) => core.Accounts.FindByToken(c.Token);

        private void SetCoins(FakeClient c, int coins)
        {
            var a = AccountOf(c);
            core.Accounts.AddCoins(a, coins - a.Coins);
        }

        // ------------------------------------------------------------------ avatar

        [Test]
        public void Avatar_DoHello_FicaNaConta_ENoPerfil()
        {
            var spec = new AvatarSpec { [AvatarSpec.Part.Hair] = 3, [AvatarSpec.Part.Skin] = 4 }.Encode();
            var ana = Client("Ana", spec);
            Assert.That(AccountOf(ana).Avatar, Is.EqualTo(spec));
            Assert.That(ana.Profile.avatar, Is.EqualTo(spec));
        }

        [Test]
        public void Avatar_Invalido_EhIgnorado_EGanhaUmSorteado()
        {
            var ana = Client("Ana", "<script>");
            Assert.That(AccountOf(ana).Avatar, Is.Empty);
            Assert.That(AvatarSpec.TryParse(ana.Profile.avatar, out _), Is.True, "Perfil sempre traz um avatar válido.");

            ana.Do(Protocol.ToServer.SetAvatar, new AvatarMsg { avatar = "1-99-0-0-0-0-0-0-0-0" });
            Assert.That(ana.LastError.code, Is.EqualTo(ErrorCodes.BadMessage));
        }

        [Test]
        public void Avatar_AparaceNaSala_ENaPartida_InclusiveDosBots()
        {
            var spec = new AvatarSpec { [AvatarSpec.Part.Mouth] = 1 }.Encode();
            var ana = Client("Ana", spec);
            ana.Create(new RoomOptions { MaxPlayers = 3 });
            ana.Do(Protocol.ToServer.AddBot);
            Assert.That(ana.Room.members[0].avatar, Is.EqualTo(spec));
            Assert.That(AvatarSpec.TryParse(ana.Room.members[1].avatar, out _), Is.True);

            // Trocar o avatar no lobby atualiza a sala na hora.
            var other = new AvatarSpec { [AvatarSpec.Part.Mouth] = 2 }.Encode();
            ana.Do(Protocol.ToServer.SetAvatar, new AvatarMsg { avatar = other });
            Assert.That(ana.Room.members[0].avatar, Is.EqualTo(other));

            ana.Do(Protocol.ToServer.StartMatch);
            Assert.That(ana.Match.avatars.Count, Is.EqualTo(2));
            Assert.That(ana.Match.avatars[0], Is.EqualTo(other));
            Assert.That(ana.Match.avatars[1], Is.EqualTo(ana.Room.members[1].avatar));
        }

        // ------------------------------------------------------------------ loja

        [Test]
        public void ItensGratis_SaoDeTodos_EVemEquipados()
        {
            var ana = Client("Ana");
            Assert.That(ana.Profile.table, Is.EqualTo(ShopCatalog.DefaultTable));
            Assert.That(ana.Profile.deck, Is.EqualTo(ShopCatalog.DefaultDeck));
            Assert.That(ana.Profile.owned, Is.Empty);
        }

        [Test]
        public void Comprar_DescontaMoedas_EPassaAUsar()
        {
            var ana = Client("Ana");
            ana.Do(Protocol.ToServer.BuyItem, new ItemMsg { item = "mesa_azul" });
            Assert.That(ana.Profile.coins, Is.EqualTo(200));
            Assert.That(ana.Profile.delta, Is.EqualTo(-300));
            Assert.That(ana.Profile.reason, Is.EqualTo("compra"));
            Assert.That(ana.Profile.owned, Has.Member("mesa_azul"));
            Assert.That(ana.Profile.table, Is.EqualTo("mesa_azul"));
        }

        [Test]
        public void Comprar_SemMoedas_Recusa_ENaoMudaNada()
        {
            var ana = Client("Ana");
            ana.Do(Protocol.ToServer.BuyItem, new ItemMsg { item = "mesa_cassino" });
            Assert.That(ana.LastError.code, Is.EqualTo(ErrorCodes.NotEnoughCoins));
            Assert.That(AccountOf(ana).Coins, Is.EqualTo(500));
            Assert.That(AccountOf(ana).Owned, Is.Empty);
        }

        [Test]
        public void Comprar_DuasVezes_OuItemInexistente_Recusa()
        {
            var ana = Client("Ana");
            ana.Do(Protocol.ToServer.BuyItem, new ItemMsg { item = "baralho_azul" });
            ana.Do(Protocol.ToServer.BuyItem, new ItemMsg { item = "baralho_azul" });
            Assert.That(ana.LastError.code, Is.EqualTo(ErrorCodes.Shop));
            Assert.That(AccountOf(ana).Coins, Is.EqualTo(200), "Cobrou só uma vez.");

            ana.Do(Protocol.ToServer.BuyItem, new ItemMsg { item = "mesa_de_graca" });
            Assert.That(ana.LastError.code, Is.EqualTo(ErrorCodes.Shop));
            ana.Do(Protocol.ToServer.BuyItem, new ItemMsg { item = ShopCatalog.DefaultTable });
            Assert.That(AccountOf(ana).Coins, Is.EqualTo(200), "Item grátis não se compra.");
        }

        [Test]
        public void Usar_SoItensQueTem()
        {
            var ana = Client("Ana");
            ana.Do(Protocol.ToServer.EquipItem, new ItemMsg { item = "baralho_preto" });
            Assert.That(ana.LastError.code, Is.EqualTo(ErrorCodes.Shop));
            Assert.That(AccountOf(ana).DeckTheme, Is.EqualTo(ShopCatalog.DefaultDeck));

            ana.Do(Protocol.ToServer.BuyItem, new ItemMsg { item = "baralho_azul" });
            ana.Do(Protocol.ToServer.EquipItem, new ItemMsg { item = ShopCatalog.DefaultDeck });
            Assert.That(ana.Profile.deck, Is.EqualTo(ShopCatalog.DefaultDeck));
            ana.Do(Protocol.ToServer.EquipItem, new ItemMsg { item = "baralho_azul" });
            Assert.That(ana.Profile.deck, Is.EqualTo("baralho_azul"));
        }

        [Test]
        public void CompraEAvatar_SaoGravados_EVoltamDepoisDeReiniciar()
        {
            var saved = new List<Account>();
            var book = new AccountBook();
            book.Persist = a => saved.Add(a);
            core = new ServerCore(new ServerOptions { Pacing = PacingSettings.Instant }, new SystemRandomSource(1), book);
            var spec = new AvatarSpec { [AvatarSpec.Part.Beard] = 3 }.Encode();
            var ana = Client("Ana", spec);
            SetCoins(ana, 1000);
            ana.Do(Protocol.ToServer.BuyItem, new ItemMsg { item = "baralho_rosa" });

            var last = saved.Last();
            Assert.That(last.Owned, Has.Member("baralho_rosa"));
            Assert.That(last.DeckTheme, Is.EqualTo("baralho_rosa"));
            Assert.That(last.Avatar, Is.EqualTo(spec));

            // O "banco" guarda uma cópia: comprar outra coisa depois não muda a gravação anterior.
            ana.Do(Protocol.ToServer.BuyItem, new ItemMsg { item = "mesa_azul" });
            Assert.That(last.Owned.Count, Is.EqualTo(1));

            var restarted = new ServerCore(new ServerOptions(), new SystemRandomSource(2), new AccountBook(new[] { saved.Last() }));
            var again = new FakeClient(restarted, "Ana").Hello(ana.Token);
            Assert.That(again.Profile.owned, Is.EquivalentTo(new[] { "baralho_rosa", "mesa_azul" }));
            Assert.That(again.Profile.avatar, Is.EqualTo(spec));
        }

        [Test]
        public void ItensSalvos_InvalidosOuDeOutraCategoria_VoltamAoPadrao()
        {
            var owned = PostgresAccountStore.SplitOwned("mesa_azul,nao_existe,mesa_azul,,baralho_preto");
            Assert.That(owned, Is.EqualTo(new[] { "mesa_azul", "baralho_preto" }));
            Assert.That(ShopCatalog.ValidEquipped(ShopCategory.Table, "baralho_preto", owned), Is.EqualTo(ShopCatalog.DefaultTable));
            Assert.That(ShopCatalog.ValidEquipped(ShopCategory.Table, "mesa_roxa", owned), Is.EqualTo(ShopCatalog.DefaultTable));
            Assert.That(ShopCatalog.ValidEquipped(ShopCategory.Table, "mesa_azul", owned), Is.EqualTo("mesa_azul"));
        }

        // ------------------------------------------------------------------ código VIP

        [Test]
        public void CodigoVip_Certo_TiraOsAnuncios()
        {
            var ana = Client("Ana");
            Assert.That(ana.Profile.noAds, Is.False);
            ana.Do(Protocol.ToServer.RedeemCode, new CodeMsg { code = "  segredo-123 " });
            Assert.That(ana.Profile.noAds, Is.True);
            Assert.That(ana.Profile.reason, Is.EqualTo("vip"));
            Assert.That(AccountOf(ana).NoAds, Is.True);
        }

        [Test]
        public void CodigoVip_Errado_Recusa_EBloqueiaDepoisDeVariasTentativas()
        {
            var ana = Client("Ana");
            for (int i = 0; i < 5; i++) ana.Do(Protocol.ToServer.RedeemCode, new CodeMsg { code = "chute" + i });
            Assert.That(ana.LastError.code, Is.EqualTo(ErrorCodes.Code));

            // Bloqueado: nem o código certo passa agora.
            ana.Do(Protocol.ToServer.RedeemCode, new CodeMsg { code = Vip });
            Assert.That(AccountOf(ana).NoAds, Is.False);
            Assert.That(ana.LastError.message, Does.Contain("tentativas"));

            core.Tick(601f);
            ana.Do(Protocol.ToServer.RedeemCode, new CodeMsg { code = Vip });
            Assert.That(AccountOf(ana).NoAds, Is.True);
        }

        [Test]
        public void CodigoVip_SemCodigoConfigurado_NadaPassa()
        {
            core = new ServerCore(new ServerOptions { Pacing = PacingSettings.Instant }, new SystemRandomSource(3));
            var ana = Client("Ana");
            ana.Do(Protocol.ToServer.RedeemCode, new CodeMsg { code = "" });
            Assert.That(ana.LastError.code, Is.EqualTo(ErrorCodes.Code));
            Assert.That(AccountOf(ana).NoAds, Is.False);
        }
    }
}
