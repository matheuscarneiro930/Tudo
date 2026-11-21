using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Fodinha.EditorTools
{
    /// <summary>
    /// Gera builds do jogo por código.
    /// Windows (para testar no PC, janela em formato de celular):
    ///   -executeMethod Fodinha.EditorTools.BuildTool.BuildWindows
    /// </summary>
    public static class BuildTool
    {
        public const string WindowsOutput = "Builds/Windows/FodinhaOnline.exe";

        [MenuItem("Fodinha/Gerar build Windows (teste no PC)")]
        public static void BuildWindows()
        {
            if (!Prepare()) return;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 540;
            PlayerSettings.defaultScreenHeight = 960;
            PlayerSettings.resizableWindow = true;

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            string output = ArgValue("-buildOutput") ?? WindowsOutput;
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            Report(report);
        }

        public const string AndroidOutput = "Builds/Android/FodinhaOnline.apk";
        public const string Version = "0.1.0";
        public const int VersionCode = 1;

        /// <summary>
        /// APK para instalar direto no celular (assinado com a chave de depuração; para a Play Store será preciso
        /// uma chave própria e o formato .aab).
        ///   -executeMethod Fodinha.EditorTools.BuildTool.BuildAndroid
        /// </summary>
        [MenuItem("Fodinha/Gerar APK Android")]
        public static void BuildAndroid()
        {
            ProjectConfigurator.Apply();
            SceneBuilder.UpdateBuildSettings();

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            if (!ValidateScenes()) { if (Application.isBatchMode) EditorApplication.Exit(1); return; }

            // Usa as ferramentas Android instaladas junto com o Unity (módulos do Hub).
            string androidPlayer = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer");
            string jdk = Path.Combine(androidPlayer, "OpenJDK");
            if (Directory.Exists(jdk)) UnityEditor.Android.AndroidExternalToolsSettings.jdkRootPath = jdk;
            string sdk = Path.Combine(androidPlayer, "SDK");
            if (Directory.Exists(sdk)) UnityEditor.Android.AndroidExternalToolsSettings.sdkRootPath = sdk;
            string ndk = Path.Combine(androidPlayer, "NDK");
            if (Directory.Exists(ndk)) UnityEditor.Android.AndroidExternalToolsSettings.ndkRootPath = ndk;

            // Anúncios: App ID do AdMob e bibliotecas Android do Google (Play Services Ads).
            AdMobConfigurator.Apply();
            AdMobConfigurator.ResolveAndroidDependencies();

            var android = UnityEditor.Build.NamedBuildTarget.Android;
            PlayerSettings.bundleVersion = Version;
            PlayerSettings.Android.bundleVersionCode = VersionCode;
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Minimal);
            EditorUserBuildSettings.buildAppBundle = false;

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            string output = ArgValue("-buildOutput") ?? AndroidOutput;
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.None
            });
            Report(report);
        }

        /// <summary>A versão web vai direto para o servidor, que a publica em "/".</summary>
        public const string WebGLOutput = "Server/Fodinha.Server/wwwroot";

        /// <summary>
        /// Versão web (abre no navegador, inclusive no iPhone). Sai em Server/Fodinha.Server/wwwroot.
        ///   -executeMethod Fodinha.EditorTools.BuildTool.BuildWebGL
        /// </summary>
        [MenuItem("Fodinha/Gerar versão web (WebGL)")]
        public static void BuildWebGL()
        {
            ProjectConfigurator.Apply();
            SceneBuilder.UpdateBuildSettings();

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
            if (!ValidateScenes()) { if (Application.isBatchMode) EditorApplication.Exit(1); return; }

            PlayerSettings.WebGL.template = "PROJECT:Fodinha";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = false; // o servidor envia Content-Encoding
            PlayerSettings.WebGL.nameFilesAsHashes = true;      // permite cache longo no navegador
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.defaultWebScreenWidth = 540;
            PlayerSettings.defaultWebScreenHeight = 960;
            PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.Minimal);

            string output = ArgValue("-buildOutput") ?? WebGLOutput;
            // Limpa o build anterior (os nomes mudam a cada versão por causa do hash).
            if (Directory.Exists(Path.Combine(output, "Build"))) Directory.Delete(Path.Combine(output, "Build"), true);
            if (Directory.Exists(Path.Combine(output, "TemplateData"))) Directory.Delete(Path.Combine(output, "TemplateData"), true);
            Directory.CreateDirectory(output);

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });
            Report(report);
        }

        /// <summary>
        /// Garante que nenhum componente das cenas do build está com o script "perdido" (o que deixa a tela vazia).
        /// Se encontrar, reimporta os scripts e confere de novo; se continuar, interrompe o build.
        /// </summary>
        public static bool ValidateScenes()
        {
            int missing = CountMissingScripts();
            if (missing > 0)
            {
                Debug.LogWarning("[Fodinha] " + missing + " componente(s) sem script nas cenas; reimportando scripts...");
                AssetDatabase.ImportAsset("Assets/Fodinha/Scripts", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                missing = CountMissingScripts();
            }
            if (missing > 0)
            {
                Debug.LogError("[Fodinha] Build cancelado: " + missing + " componente(s) continuam sem script. Rode de novo.");
                return false;
            }
            Debug.Log("[Fodinha] Cenas conferidas: todos os scripts presentes.");
            return true;
        }

        private static int CountMissingScripts()
        {
            int total = 0;
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (!s.enabled) continue;
                var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(s.path, UnityEditor.SceneManagement.OpenSceneMode.Single);
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        int n = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                        if (n > 0) Debug.LogWarning("[Fodinha] Script perdido em '" + t.name + "' (" + s.path + ")");
                        total += n;
                    }
            }
            return total;
        }

        private static bool Prepare()
        {
            ProjectConfigurator.Apply();
            SceneBuilder.UpdateBuildSettings();
            if (ValidateScenes()) return true;
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return false;
        }

        /// <summary>Valor de um argumento da linha de comando (ex.: -buildOutput caminho).</summary>
        public static string ArgValue(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        private static void Report(BuildReport report)
        {
            var s = report.summary;
            Debug.Log("[Fodinha] Build " + s.platform + ": " + s.result + " (" + s.totalErrors + " erros, " +
                      (s.totalSize / (1024 * 1024)) + " MB) em " + s.outputPath);

            // Script perdido no empacotamento = jogo abre com tela vazia. Trata como falha.
            int missing = 0;
            foreach (var step in report.steps)
                foreach (var m in step.messages)
                    if (m.content != null && m.content.Contains("is missing or no valid script")) missing++;
            if (missing > 0) Debug.LogError("[Fodinha] BUILD COM DEFEITO: " + missing + " script(s) perdido(s) nas cenas. Não publique.");

            if ((s.result != BuildResult.Succeeded || missing > 0) && Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
