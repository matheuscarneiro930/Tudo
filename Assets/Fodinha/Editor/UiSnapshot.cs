using System;
using System.IO;
using Fodinha.Core;
using Fodinha.Game;
using Fodinha.Game.UI;
using Fodinha.Hosting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fodinha.EditorTools
{
    /// <summary>
    /// Gera capturas PNG da tela de partida em vários cenários (2 a 15 jogadores, aposta, jogada, vaza, rodada, fim).
    /// Serve para revisar o layout sem abrir o celular.
    /// Batchmode (sem -nographics): -executeMethod Fodinha.EditorTools.UiSnapshot.CaptureFromCommandLine -snapshotDir &lt;pasta&gt;
    /// </summary>
    public static class UiSnapshot
    {
        private struct Scenario
        {
            public string Name;
            public int Players;
            public int Seed;
            public bool HumanAuto;
            public Func<MatchView, bool> Target;
        }

        [MenuItem("Fodinha/Capturar telas de teste")]
        public static void CaptureFromMenu()
        {
            string dir = Path.GetFullPath("Temp/Capturas");
            Capture(dir, 1080, 1920);
            EditorUtility.RevealInFinder(dir);
        }

        public static void CaptureFromCommandLine()
        {
            string dir = Path.GetFullPath("Temp/Capturas");
            int width = 1080, height = 1920;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-snapshotDir") dir = args[i + 1];
                if (args[i] == "-snapshotSize")
                {
                    var parts = args[i + 1].Split('x');
                    width = int.Parse(parts[0]);
                    height = int.Parse(parts[1]);
                }
            }
            Capture(dir, width, height);
        }

        public static void Capture(string folder, int width, int height)
        {
            Directory.CreateDirectory(folder);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var scenarios = new[]
            {
                new Scenario { Name = "01_4j_cega", Players = 4, Seed = 11,
                    Target = v => v.Phase == MatchPhase.Betting && v.IsBlindRound && v.IsMyTurn },
                new Scenario { Name = "02_4j_aposta", Players = 4, Seed = 12,
                    Target = v => v.Phase == MatchPhase.Betting && v.CardsThisRound >= 3 && v.IsMyTurn },
                new Scenario { Name = "03_4j_jogada", Players = 4, Seed = 13,
                    Target = v => v.Phase == MatchPhase.Playing && v.CardsThisRound >= 3 && v.IsMyTurn && v.CurrentTrick.Count >= 2 },
                new Scenario { Name = "04_8j_vaza", Players = 8, Seed = 14,
                    Target = v => v.Phase == MatchPhase.TrickComplete && v.CardsThisRound >= 2 },
                new Scenario { Name = "05_15j_jogada", Players = 15, Seed = 15,
                    Target = v => v.Phase == MatchPhase.Playing && v.CardsThisRound >= 2 && v.IsMyTurn && v.CurrentTrick.Count >= 6 },
                new Scenario { Name = "06_15j_cega", Players = 15, Seed = 16,
                    Target = v => v.Phase == MatchPhase.Betting && v.IsBlindRound && v.IsMyTurn },
                new Scenario { Name = "07_6j_rodada", Players = 6, Seed = 17,
                    Target = v => v.Phase == MatchPhase.RoundComplete && v.RoundNumber >= 3 },
                new Scenario { Name = "08_2j_aposta", Players = 2, Seed = 18,
                    Target = v => v.Phase == MatchPhase.Betting && v.CardsThisRound >= 3 && v.IsMyTurn },
                new Scenario { Name = "09_5j_fim", Players = 5, Seed = 19, HumanAuto = true,
                    Target = v => v.Phase == MatchPhase.MatchOver },
            };

            foreach (var s in scenarios)
            {
                try
                {
                    CaptureScenario(s, folder, width, height);
                }
                catch (Exception e)
                {
                    Debug.LogError("[Fodinha] Falha na captura " + s.Name + ": " + e);
                }
            }
            Debug.Log("[Fodinha] Capturas salvas em " + folder);
        }

        private static void CaptureScenario(Scenario s, string folder, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var camGo = new GameObject("CameraCaptura");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.orthographic = true;
            cam.targetTexture = rt;

            var canvasGo = new GameObject("CanvasCaptura", typeof(RectTransform));
            canvasGo.layer = UiKit.UiLayer;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 5f;
            float scale = Mathf.Min(width / SceneBuilder.ReferenceResolution.x, height / SceneBuilder.ReferenceResolution.y);
            canvas.scaleFactor = scale;

            var logic = new GameObject("Partida");
            var controller = logic.AddComponent<LocalMatchController>();
            var screenRt = UiKit.Rect("TelaPartida", canvas.transform).Fill();
            var screen = screenRt.gameObject.AddComponent<MatchScreen>();

            try
            {
                Canvas.ForceUpdateCanvases();
                var cfg = new OfflineMatchConfig { BotCount = s.Players - 1, Seed = s.Seed, PlayerName = "Você" };
                controller.StartMatch(cfg, PacingSettings.Instant);
                if (s.HumanAuto) controller.SetHumanAutoPlay(true);
                screen.Bind(controller);

                if (!MatchDriver.AdvanceUntil(controller, s.Target))
                    Debug.LogWarning("[Fodinha] Cenário " + s.Name + " não atingiu o estado-alvo; capturando o estado atual.");

                Canvas.ForceUpdateCanvases();
                screen.Render(controller.View);
                Canvas.ForceUpdateCanvases();

                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(folder, s.Name + ".png"), tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasGo);
                UnityEngine.Object.DestroyImmediate(logic);
                UnityEngine.Object.DestroyImmediate(camGo);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }
    }
}
