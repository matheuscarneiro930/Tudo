using Fodinha.Hosting;
using UnityEngine;
using UnityEngine.UI;

namespace Fodinha.Game.UI
{
    /// <summary>Avatar redondo na interface. Só redesenha quando o avatar muda.</summary>
    public sealed class AvatarView : MonoBehaviour
    {
        private Image image;
        private Sprite shared;      // do cache do AvatarPainter (compartilhado; contado para não ser apagado em uso)
        private Sprite ownPreview;  // prévia do editor: só desta tela, apagada ao trocar

        public string Current { get; private set; }
        public Image Image => image;

        public static AvatarView Create(Transform parent, string name = "Avatar")
        {
            var img = UiKit.Image(name, parent, Color.white, null);
            img.preserveAspect = true;
            img.raycastTarget = false;
            var view = img.gameObject.AddComponent<AvatarView>();
            view.image = img;
            return view;
        }

        /// <summary>Mostra o avatar (texto do AvatarSpec). Inválido/vazio = avatar sorteado por <paramref name="seed"/>.</summary>
        public void Show(string encoded, string seed = "")
        {
            string key = (encoded ?? "") + "|" + (seed ?? "");
            if (key == Current && image.sprite != null) return;
            Current = key;
            var sprite = AvatarPainter.Get(encoded, seed);
            AvatarPainter.Retain(sprite);
            ReleaseAll();
            shared = sprite;
            image.sprite = sprite;
        }

        /// <summary>
        /// Prévia do editor de avatar: desenhada só para esta tela (não entra no cache). Assim, testar dezenas de
        /// combinações não enche a memória nem tira do cache os avatares que estão na mesa.
        /// </summary>
        public void ShowPreview(AvatarSpec spec)
        {
            string key = "previa|" + spec.Encode();
            if (key == Current && image.sprite != null) return;
            Current = key;
            var tex = AvatarPainter.Paint(spec, AvatarPainter.PreviewSize);
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.hideFlags = HideFlags.DontSave;
            ReleaseAll();
            ownPreview = sprite;
            image.sprite = sprite;
        }

        private void ReleaseAll()
        {
            if (shared != null) AvatarPainter.Release(shared);
            shared = null;
            if (ownPreview != null)
            {
                var tex = ownPreview.texture;
                DestroySafe(ownPreview);
                if (tex != null) DestroySafe(tex);
            }
            ownPreview = null;
        }

        private static void DestroySafe(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        private void OnDestroy() => ReleaseAll();
    }
}
