using System.Collections.Generic;
using Fodinha.Game.UI;
using Fodinha.Hosting;
using NUnit.Framework;
using UnityEngine;

namespace Fodinha.Game.Tests
{
    /// <summary>A memória do app não cresce sem limite e nada que está na tela é apagado.</summary>
    public class MemoryTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        private AvatarView NewView()
        {
            var go = new GameObject("Teste", typeof(RectTransform));
            created.Add(go);
            return AvatarView.Create(go.transform);
        }

        [Test]
        public void EditorDeAvatar_TestarMuitasCombinacoes_NaoEncheOCache()
        {
            int before = AvatarPainter.CachedCount;
            var view = NewView();
            var rng = new System.Random(1);
            for (int i = 0; i < 40; i++) view.ShowPreview(AvatarSpec.Random(rng));
            Assert.That(AvatarPainter.CachedCount, Is.EqualTo(before));
            Assert.That(view.Image.sprite, Is.Not.Null);
        }

        [Test]
        public void CacheCheio_NaoApagaAvatarQueEstaNaTela()
        {
            var onScreen = new List<AvatarView>();
            for (int i = 0; i < 15; i++)
            {
                var v = NewView();
                v.Show("", "Jogador" + i);
                onScreen.Add(v);
            }
            // Muitos avatares diferentes passando (lobbies, salas...) enchem o cache.
            for (int i = 0; i < 120; i++) AvatarPainter.Get(AvatarSpec.FromSeed("Outro" + i));

            Assert.That(AvatarPainter.CachedCount, Is.LessThanOrEqualTo(AvatarPainter.MaxCached));
            foreach (var v in onScreen) Assert.That(v.Image.sprite != null, Is.True, "Avatar que está na tela continua lá.");
        }

        [Test]
        public void Loja_UsaMiniaturas_ESoAMesaEmUsoFicaGrande()
        {
            var thumb = SpriteFactory.TableThumbFor("mesa_roxa");
            Assert.That(thumb.texture.width, Is.LessThanOrEqualTo(128));
            Assert.That(SpriteFactory.Table.texture.width, Is.EqualTo(512));
        }
    }
}
