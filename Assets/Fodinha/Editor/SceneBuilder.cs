using System.Collections.Generic;
using System.IO;
using Fodinha.Game;
using Fodinha.Game.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Fodinha.EditorTools
{
    /// <summary>
    /// Monta as cenas do jogo por código (câmera, EventSystem, Canvas em retrato, componentes e referências).
    /// Pode ser chamado pelo menu "Fodinha/Montar cenas" ou em batchmode:
    ///   Unity -batchmode -quit -projectPath . -executeMethod Fodinha.EditorTools.SceneBuilder.BuildAll
    /// </summary>
    public static class SceneBuilder
    {
        public const string ScenesFolder = "Assets/Fodinha/Scenes";
        public const string OfflineScenePath = ScenesFolder + "/PartidaOffline.unity";
        public const string MenuScenePath = ScenesFolder + "/Menu.unity";
        public const string MenuSceneName = "Menu";
        public const string OfflineSceneName = "PartidaOffline";
        public static readonly Vector2 ReferenceResolution = new Vector2(1080f, 1920f);

        [MenuItem("Fodinha/Montar cenas")]
        public static void BuildAll()
        {
            ProjectConfigurator.Apply();
            Directory.CreateDirectory(ScenesFolder);
            BuildMenuScene();
            BuildOfflineScene();
            UpdateBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Fodinha] Cenas montadas.");
        }

        public static void BuildOfflineScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera();
            CreateEventSystem();
            var canvas = CreateCanvas("Canvas");

            var screenRt = UiKit.Rect("TelaPartida", canvas.transform).Fill();
            var screen = screenRt.gameObject.AddComponent<MatchScreen>();

            var logic = new GameObject("PartidaOffline");
            var controller = logic.AddComponent<LocalMatchController>();
            var root = logic.AddComponent<OfflineGameRoot>();
            root.Configure(controller, screen, MenuSceneName);

            var so = new SerializedObject(screen);
            so.FindProperty("sessionSource").objectReferenceValue = controller;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(root);

            EditorSceneManager.SaveScene(scene, OfflineScenePath);
        }

        public static void BuildMenuScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera();
            CreateEventSystem();
            var canvas = CreateCanvas("Canvas");

            var screenRt = UiKit.Rect("TelaMenu", canvas.transform).Fill();
            var screen = screenRt.gameObject.AddComponent<Fodinha.Game.Menu.MenuScreen>();

            var logic = new GameObject("Menu");
            var root = logic.AddComponent<Fodinha.Game.Menu.MenuRoot>();
            root.Configure(screen, OfflineSceneName);
            EditorUtility.SetDirty(root);

            EditorSceneManager.SaveScene(scene, MenuScenePath);
        }

        public static Camera CreateCamera()
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.BackgroundBottom;
            cam.orthographic = true;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            camGo.AddComponent<AudioListener>();
            return cam;
        }

        public static void CreateEventSystem()
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        public static Canvas CreateCanvas(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UiKit.UiLayer;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            // Expand: a tela sempre tem pelo menos 1080x1920 unidades (celulares mais altos ganham espaço vertical).
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>Coloca as cenas na lista de build (o menu, quando existir, vem primeiro).</summary>
        public static void UpdateBuildSettings()
        {
            var ordered = new List<string>();
            foreach (var path in ScenePathsInBuildOrder())
                if (File.Exists(path)) ordered.Add(path);

            var scenes = new List<EditorBuildSettingsScene>();
            foreach (var path in ordered) scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>Cenas em ordem de build. As próximas etapas acrescentam suas cenas aqui.</summary>
        public static IEnumerable<string> ScenePathsInBuildOrder()
        {
            yield return MenuScenePath;
            yield return OfflineScenePath;
        }
    }
}
