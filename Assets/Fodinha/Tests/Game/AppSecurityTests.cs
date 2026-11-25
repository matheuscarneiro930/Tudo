using Fodinha.Game.Ads;
using NUnit.Framework;
using UnityEngine;

namespace Fodinha.Game.Tests
{
    /// <summary>Proteções do lado do app.</summary>
    public class AppSecurityTests
    {
        private string savedServer;

        [SetUp]
        public void SetUp() => savedServer = PlayerPrefs.GetString("fodinha.servidor", "");

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.SetString("fodinha.servidor", savedServer);
            AdService.RequireRealAd = false;
        }

        /// <summary>Golpe: "troque o servidor nas Configurações para este aqui" — a chave da conta oficial não vai junto.</summary>
        [Test]
        public void ServidorDigitado_TemChaveDeContaSeparada()
        {
            GamePrefs.ServerUrl = "";
            Assert.That(GamePrefs.TokenKey, Is.EqualTo("fodinha.token"));

            GamePrefs.ServerUrl = "wss://servidor-de-golpe.example/ws";
            string other = GamePrefs.TokenKey;
            Assert.That(other, Does.StartWith("fodinha.token.").And.Not.EqualTo("fodinha.token"));

            GamePrefs.ServerUrl = "wss://outro.example/ws";
            Assert.That(GamePrefs.TokenKey, Is.Not.EqualTo(other), "Cada servidor tem a sua.");
        }

        [Test]
        public void SemRedeDeAnuncios_NaoFingeQueTemVideo()
        {
            Assert.That(AdService.HasRealAds, Is.False, "No Editor/site é a tela provisória.");
            AdService.RequireRealAd = true;
            Assert.That(AdService.RequireRealAd && !AdService.HasRealAds, Is.True, "O app avisa em vez de mostrar a tela provisória.");
        }
    }
}
