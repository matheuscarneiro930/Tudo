using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Fodinha.Game
{
    /// <summary>
    /// Compartilhar um texto pelo menu do aparelho (WhatsApp, SMS...).
    /// Android: menu nativo de compartilhar. Site: menu do navegador (iPhone/Android) ou copia o texto.
    /// Editor/PC: copia o texto.
    /// </summary>
    public static class ShareService
    {
        public enum Result { MenuOpened, Copied, Failed }

        /// <summary>Último texto enviado para compartilhar (para testes).</summary>
        public static string LastShared { get; private set; }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int FodinhaShare_Text(string text);
#endif

        public static Result Share(string text, string title = "Fodinha Online")
        {
            LastShared = text;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var intentClass = new AndroidJavaClass("android.content.Intent"))
                using (var intent = new AndroidJavaObject("android.content.Intent"))
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    intent.Call<AndroidJavaObject>("setAction", intentClass.GetStatic<string>("ACTION_SEND"));
                    intent.Call<AndroidJavaObject>("setType", "text/plain");
                    intent.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_TEXT"), text);
                    intent.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_SUBJECT"), title);
                    using (var chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, "Convidar pelo..."))
                        activity.Call("startActivity", chooser);
                }
                return Result.MenuOpened;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Fodinha] Compartilhar falhou: " + e.Message);
                GUIUtility.systemCopyBuffer = text;
                return Result.Copied;
            }
#elif UNITY_WEBGL && !UNITY_EDITOR
            // 1 = abriu o menu do navegador; 2 = copiou; 0 = não deu.
            int r = FodinhaShare_Text(text);
            return r == 1 ? Result.MenuOpened : r == 2 ? Result.Copied : Result.Failed;
#else
            GUIUtility.systemCopyBuffer = text;
            return Result.Copied;
#endif
        }
    }
}
