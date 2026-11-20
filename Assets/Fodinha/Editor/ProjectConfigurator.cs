using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Fodinha.EditorTools
{
    /// <summary>Configurações do projeto aplicadas por código (nome do app, retrato, identificador).</summary>
    public static class ProjectConfigurator
    {
        public const string ProductName = "Fodinha Online";
        public const string CompanyName = "Fodinha Online";
        public const string AppIdentifier = "com.fodinhaonline.app";

        [MenuItem("Fodinha/Configurar projeto (retrato, nome do app)")]
        public static void Apply()
        {
            PlayerSettings.productName = ProductName;
            PlayerSettings.companyName = CompanyName;

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AppIdentifier);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, AppIdentifier);

            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();
            Debug.Log("[Fodinha] Projeto configurado: " + ProductName + " (" + AppIdentifier + "), retrato.");
        }
    }
}
