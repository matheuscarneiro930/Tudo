using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Fodinha.EditorTools
{
    /// <summary>
    /// Configura o SDK do AdMob por código: App ID (teste enquanto não houver o real) e resolução das
    /// dependências Android (Google Play Services) antes do build.
    /// </summary>
    public static class AdMobConfigurator
    {
        /// <summary>App IDs reais (preencher quando a conta AdMob existir). Vazio = App ID de teste do Google.</summary>
        public const string AndroidAppIdReal = "";
        public const string IosAppIdReal = "";

        [MenuItem("Fodinha/Configurar AdMob")]
        public static void Apply()
        {
            var type = Type.GetType("GoogleMobileAds.Editor.GoogleMobileAdsSettings, GoogleMobileAds.Editor");
            var load = type?.GetMethod("LoadInstance", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (!(load?.Invoke(null, null) is ScriptableObject settings))
            {
                Debug.LogWarning("[Fodinha] SDK do AdMob não encontrado; anúncios desativados.");
                return;
            }

            var so = new SerializedObject(settings);
            so.FindProperty("adMobAndroidAppId").stringValue =
                string.IsNullOrEmpty(AndroidAppIdReal) ? Fodinha.Ads.AdIds.AndroidAppTest : AndroidAppIdReal;
            so.FindProperty("adMobIOSAppId").stringValue =
                string.IsNullOrEmpty(IosAppIdReal) ? Fodinha.Ads.AdIds.IosAppTest : IosAppIdReal;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log("[Fodinha] AdMob configurado (" + (string.IsNullOrEmpty(AndroidAppIdReal) ? "IDs de TESTE" : "IDs reais") + ").");
        }

        /// <summary>Baixa/atualiza as bibliotecas Android do AdMob (External Dependency Manager).</summary>
        public static void ResolveAndroidDependencies()
        {
            var resolver = Type.GetType("GooglePlayServices.PlayServicesResolver, Google.JarResolver");
            var resolve = resolver?.GetMethod("ResolveSync", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(bool) }, null);
            if (resolve == null)
            {
                Debug.LogWarning("[Fodinha] Resolvedor de dependências Android não encontrado.");
                return;
            }
            var ok = resolve.Invoke(null, new object[] { true });
            Debug.Log("[Fodinha] Dependências Android resolvidas: " + ok);
        }
    }
}
