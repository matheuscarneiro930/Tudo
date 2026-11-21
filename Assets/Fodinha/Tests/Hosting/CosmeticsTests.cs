using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Fodinha.Hosting.Tests
{
    public class AvatarSpecTests
    {
        [Test]
        public void TextoIdaEVolta_DaOMesmoAvatar()
        {
            var spec = new AvatarSpec
            {
                [AvatarSpec.Part.Skin] = 5, [AvatarSpec.Part.Hair] = 7, [AvatarSpec.Part.Beard] = 3, [AvatarSpec.Part.Background] = 2
            };
            var text = spec.Encode();
            Assert.That(text, Does.StartWith("1-"));
            Assert.That(AvatarSpec.TryParse(text, out var back), Is.True);
            Assert.That(back, Is.EqualTo(spec));
            Assert.That(text.Length, Is.LessThanOrEqualTo(AvatarSpec.MaxEncodedLength));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("abc")]
        [TestCase("2-0-0-0-0-0-0-0-0-0")]       // versão desconhecida
        [TestCase("1-0-0-0-0-0-0-0-0")]         // faltando parte
        [TestCase("1-0-0-0-0-0-0-0-0-0-0")]     // parte a mais
        [TestCase("1-6-0-0-0-0-0-0-0-0")]       // pele que não existe
        [TestCase("1--1-0-0-0-0-0-0-0-0")]
        [TestCase("1-0-0-0-0-0-0-0-0-<b>")]
        public void TextoInvalido_EhRecusado(string text)
        {
            Assert.That(AvatarSpec.TryParse(text, out _), Is.False);
            Assert.That(AvatarSpec.Sanitize(text), Is.Empty);
        }

        [Test]
        public void Indice_ForaDoLimite_EhAjustado()
        {
            var spec = new AvatarSpec { [AvatarSpec.Part.Hair] = 99, [AvatarSpec.Part.Skin] = -3 };
            Assert.That(spec[AvatarSpec.Part.Hair], Is.EqualTo(AvatarSpec.OptionCount(AvatarSpec.Part.Hair) - 1));
            Assert.That(spec[AvatarSpec.Part.Skin], Is.EqualTo(0));
        }

        [Test]
        public void Semente_SempreDaOMesmoAvatar_EVariaEntreNomes()
        {
            Assert.That(AvatarSpec.FromSeed("Zé"), Is.EqualTo(AvatarSpec.FromSeed("Zé")));
            var distinct = new HashSet<string>();
            foreach (var name in BotNames.Pick(14, 3)) distinct.Add(AvatarSpec.FromSeed(name).Encode());
            Assert.That(distinct.Count, Is.GreaterThan(10), "Bots diferentes devem ter rostos diferentes.");
            Assert.That(AvatarSpec.TryParse(AvatarSpec.FromSeed("").Encode(), out _), Is.True);
        }

        [Test]
        public void CadaParte_TemNomeEOpcoes()
        {
            Assert.That(AvatarSpec.PartLabels.Length, Is.EqualTo(AvatarSpec.PartCount));
            Assert.That(AvatarSpec.OptionLabels.Length, Is.EqualTo(AvatarSpec.PartCount));
            foreach (var options in AvatarSpec.OptionLabels) Assert.That(options.Length, Is.InRange(2, 10));
        }
    }

    public class ShopCatalogTests
    {
        [Test]
        public void Catalogo_IdsUnicos_PrecosValidos_UmGratisPorCategoria()
        {
            var ids = ShopCatalog.Items.Select(i => i.Id).ToList();
            Assert.That(ids, Is.Unique);
            Assert.That(ShopCatalog.Items.All(i => i.Price >= 0 && i.Price <= 100000), Is.True);
            Assert.That(ShopCatalog.Find(ShopCatalog.DefaultTable).IsFree, Is.True);
            Assert.That(ShopCatalog.Find(ShopCatalog.DefaultDeck).IsFree, Is.True);
            Assert.That(ShopCatalog.Of(ShopCategory.Table).Count(i => i.IsFree), Is.EqualTo(1));
            Assert.That(ShopCatalog.Of(ShopCategory.Deck).Count(i => i.IsFree), Is.EqualTo(1));
            Assert.That(ShopCatalog.Of(ShopCategory.Table).Count, Is.GreaterThanOrEqualTo(5));
            Assert.That(ShopCatalog.Of(ShopCategory.Deck).Count, Is.GreaterThanOrEqualTo(5));
        }

        [Test]
        public void ItemEquipado_SoValeSeForDoJogador()
        {
            var owned = new List<string> { "mesa_azul" };
            Assert.That(ShopCatalog.Owns(owned, "mesa_azul"), Is.True);
            Assert.That(ShopCatalog.Owns(owned, ShopCatalog.DefaultDeck), Is.True, "Grátis é de todos.");
            Assert.That(ShopCatalog.Owns(owned, "mesa_roxa"), Is.False);
            Assert.That(ShopCatalog.Owns(null, "mesa_azul"), Is.False);
            Assert.That(ShopCatalog.ValidEquipped(ShopCategory.Table, "mesa_roxa", owned), Is.EqualTo(ShopCatalog.DefaultTable));
            Assert.That(ShopCatalog.ValidEquipped(ShopCategory.Deck, "mesa_azul", owned), Is.EqualTo(ShopCatalog.DefaultDeck));
            Assert.That(ShopCatalog.ValidEquipped(ShopCategory.Table, null, owned), Is.EqualTo(ShopCatalog.DefaultTable));
        }
    }
}
