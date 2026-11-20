using UnityEngine;

namespace Fodinha.Game.UI
{
    /// <summary>Mantém o conteúdo dentro da área segura da tela (entalhe/câmera/barra de gestos dos celulares).</summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private Rect applied;
        private Vector2Int screenSize;

        private void OnEnable() => Apply();

        private void Update()
        {
            if (Screen.safeArea != applied || screenSize.x != Screen.width || screenSize.y != Screen.height) Apply();
        }

        private void Apply()
        {
            var rt = (RectTransform)transform;
            applied = Screen.safeArea;
            screenSize = new Vector2Int(Screen.width, Screen.height);
            if (Screen.width <= 0 || Screen.height <= 0 || applied.width <= 0 || applied.height <= 0)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
            }
            else
            {
                rt.anchorMin = new Vector2(applied.xMin / Screen.width, applied.yMin / Screen.height);
                rt.anchorMax = new Vector2(applied.xMax / Screen.width, applied.yMax / Screen.height);
            }
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
