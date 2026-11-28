using System.Collections.Generic;
using Fodinha.Core;
using NUnit.Framework;

namespace Fodinha.Hosting.Tests
{
    public class RoomCodeTests
    {
        [Test]
        public void Gerado_TemSeisCaracteres_Validos()
        {
            var rng = new SystemRandomSource(1);
            var seen = new HashSet<string>();
            for (int i = 0; i < 1000; i++)
            {
                var code = RoomCode.Generate(rng);
                Assert.That(code.Length, Is.EqualTo(6));
                Assert.That(RoomCode.IsValid(code), Is.True, code);
                seen.Add(code);
            }
            Assert.That(seen.Count, Is.GreaterThan(990), "Códigos devem ser praticamente únicos.");
        }

        [Test]
        public void SemCaracteresAmbiguos()
        {
            foreach (char c in "01OIL") Assert.That(RoomCode.Alphabet.IndexOf(c), Is.EqualTo(-1), c.ToString());
        }

        [TestCase("abc234", "ABC234", true)]
        [TestCase(" ab-c2 34 ", "ABC234", true)]
        [TestCase("ABC23", "ABC23", false)]
        [TestCase("ABC2340", "ABC2340", false)]
        [TestCase("ABCD0O", "ABCD0O", false)]
        [TestCase("", "", false)]
        public void Normaliza_EValida(string input, string normalized, bool valid)
        {
            Assert.That(RoomCode.Normalize(input), Is.EqualTo(normalized));
            Assert.That(RoomCode.IsValid(RoomCode.Normalize(input)), Is.EqualTo(valid));
        }

        [Test]
        public void OpcoesDeSala_Validacao()
        {
            var o = new RoomOptions();
            Assert.That(o.GetValidationErrors(), Is.Empty);
            o.MaxPlayers = 16;
            Assert.That(o.GetValidationErrors(), Is.Not.Empty);
            o.MaxPlayers = 2;
            o.Rules.StartingLives = 9;
            Assert.That(o.GetValidationErrors(), Is.Not.Empty);
        }

        [Test]
        public void PartidaAlta_SoEmSalaPublica_EComOutroHumano()
        {
            Assert.That(RoomOptions.IsHighStake(500), Is.False);
            Assert.That(RoomOptions.IsHighStake(1000), Is.True);

            var privada = new RoomOptions { IsPublic = false, Stake = 1000 };
            Assert.That(privada.GetValidationErrors(), Has.Member(RoomOptions.HighStakeNeedsPublic));
            privada.Stake = 500;
            Assert.That(privada.GetValidationErrors(), Is.Empty, "Privada valendo 500 continua valendo.");

            var publica = new RoomOptions { IsPublic = true, Stake = 1000 };
            Assert.That(publica.GetValidationErrors(), Is.Empty);
            Assert.That(publica.CanStartWith(1), Is.False, "Só o dono (o resto bots) não começa.");
            Assert.That(publica.CanStartWith(2), Is.True);
            publica.Stake = 500;
            Assert.That(publica.CanStartWith(1), Is.True, "Até 500 dá para jogar contra bots.");
            Assert.That(RoomOptions.CanStart(1000, false, 5), Is.False);
        }

        [Test]
        public void OpcoesDeSala_CloneIndependente()
        {
            var o = new RoomOptions();
            var c = o.Clone();
            c.Rules.StartingLives = 5;
            Assert.That(o.Rules.StartingLives, Is.EqualTo(3));
        }
    }
}
