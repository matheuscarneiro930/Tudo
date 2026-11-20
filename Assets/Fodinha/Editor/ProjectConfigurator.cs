using UnityEditor;
using UnityEditor.Build;
using Fodinha.Game.UI;
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

            // Splash: fundo verde do jogo (o logo da Unity é opcional no Unity 6).
            PlayerSettings.SplashScreen.backgroundColor = Palette.BackgroundBottom;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.show = false;

            IconBuilder.Apply();
            AssetDatabase.SaveAssets();
            Debug.Log("[Fodinha] Projeto configurado: " + ProductName + " (" + AppIdentifier + "), retrato.");
        }
    }
}
