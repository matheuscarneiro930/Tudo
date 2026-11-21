using UnityEngine;
using UnityEngine.UI;

namespace Fodinha.Game.UI
{
    /// <summary>Avatar redondo na interface. Só redesenha quando o avatar muda.</summary>
    public sealed class AvatarView : MonoBehaviour
    {
        private Image image;

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
            image.sprite = AvatarPainter.Get(encoded, seed);
        }
    }
}
