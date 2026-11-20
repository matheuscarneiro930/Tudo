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
            SceneBuilder.UpdateBuildSettings();
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 540;
            PlayerSettings.defaultScreenHeight = 960;
            PlayerSettings.resizableWindow = true;

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(WindowsOutput));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = WindowsOutput,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            Report(report);
        }

        private static void Report(BuildReport report)
        {
            var s = report.summary;
            Debug.Log("[Fodinha] Build " + s.platform + ": " + s.result + " (" + s.totalErrors + " erros, " +
                      (s.totalSize / (1024 * 1024)) + " MB) em " + s.outputPath);
            if (s.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
