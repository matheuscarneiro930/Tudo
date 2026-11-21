using System;
using Fodinha.Game.Audio;
using Fodinha.Game.UI;
using NUnit.Framework;
using UnityEngine;

namespace Fodinha.Game.Tests
{
    public class PolishTests
    {
        [Test]
        public void Sons_SaoGerados_ESemEstouro()
        {
            foreach (Sound s in Enum.GetValues(typeof(Sound)))
            {
                var data = Sfx.Samples(s);
                Assert.That(data.Length, Is.GreaterThan(500), s.ToString());
                float peak = 0f;
                foreach (var v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
                Assert.That(peak, Is.GreaterThan(0.05f).And.LessThanOrEqualTo(1f), s.ToString());
            }
        }

        [Test]
        public void Animacoes_ForaDoPlayMode_AplicamOFinal()
        {
            var go = new GameObject("x", typeof(RectTransform));
            try
            {
                var rt = (RectTransform)go.transform;
                bool done = false;
                Tweens.Move(rt, new Vector2(500f, 500f), new Vector2(10f, 20f), 1f, 0.5f, Vector3.zero, Vector3.one, () => done = true);
                Assert.That(rt.anchoredPosition, Is.EqualTo(new Vector2(10f, 20f)));
                Assert.That(rt.localScale, Is.EqualTo(Vector3.one));
                Assert.That(done, Is.True);
                Assert.That(Tweens.IsAnimating(rt), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Anuncios_ForaDoCelular_UsaServicoProvisorio()
        {
            Assert.That(Fodinha.Game.Ads.AdService.Current, Is.InstanceOf<Fodinha.Game.Ads.PlaceholderAdService>());
            bool? earned = null;
            Fodinha.Game.Ads.AdService.ShowRewarded(e => earned = e);
            Assert.That(earned, Is.True, "Fora do Play Mode a recompensa provisória é imediata.");

            bool closed = false;
            int before = Fodinha.Game.Ads.AdService.InterstitialsRequested;
            Fodinha.Game.Ads.AdService.ShowInterstitial(() => closed = true);
            Assert.That(closed, Is.True, "Sem rede de anúncios, segue o jogo na hora.");
            Assert.That(Fodinha.Game.Ads.AdService.InterstitialsRequested, Is.EqualTo(before + 1));
        }

        [Test]
        public void Botao_TocaClique()
        {
            var go = new GameObject("raiz", typeof(RectTransform));
            try
            {
                int before = Sfx.PlayedCount;
                bool clicked = false;
                var b = UiKit.Button("B", go.transform, "Ok", () => clicked = true);
                b.onClick.Invoke();
                Assert.That(clicked, Is.True);
                Assert.That(Sfx.PlayedCount, Is.EqualTo(before + 1));
                Assert.That(Sfx.LastPlayed, Is.EqualTo(Sound.Click));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
