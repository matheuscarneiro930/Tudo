using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Fodinha.Game;
using Fodinha.Game.Menu;
using Fodinha.Game.UI;
using Fodinha.Hosting;
using UnityEngine;
using UnityEngine.Profiling;

namespace Fodinha.EditorTools
{
    /// <summary>
    /// Mede quanta memória o JOGO cria (texturas desenhadas por código e objetos C#) nas telas mais pesadas:
    /// menu + loja (todas as mesas/baralhos) + editor de avatar + mesa com 15 jogadores.
    /// Uso: Tools/run-unity-method.ps1 -Method Fodinha.EditorTools.MemoryReport.ReportFromCommandLine -Graphics
    ///      -Extra "-reportPath C:\caminho\memoria.txt"
    /// </summary>
    public static class MemoryReport
    {
        public static void ReportFromCommandLine()
        {
            string path = null;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-reportPath") path = args[i + 1];
            string text = Run();
            Debug.Log("[Fodinha] Memória:\n" + text);
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, text, Encoding.UTF8);
        }

        public static string Run()
        {
            var before = new HashSet<int>(Resources.FindObjectsOfTypeAll<Texture>().Select(t => t.GetInstanceID()));
            GC.Collect();
            long monoBefore = GC.GetTotalMemory(true);
            var sb = new StringBuilder();
            var created = new List<UnityEngine.Object>();

            var canvasGo = new GameObject("CanvasMemoria", typeof(RectTransform));
            canvasGo.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            created.Add(canvasGo);

            // Menu, loja (as duas abas) e editor de avatar com várias trocas.
            var menu = UiKit.Rect("Menu", canvasGo.transform).Fill().gameObject.AddComponent<MenuScreen>();
            menu.SetCoins(1250, false);
            menu.ShowPage(MenuScreen.Page.Shop);
            menu.SetShopTab(ShopCategory.Deck);
            menu.ShowPage(MenuScreen.Page.Avatar);
            var rng = new System.Random(3);
            for (int i = 0; i < 30; i++) menu.AvatarSteppers[rng.Next(menu.AvatarSteppers.Count)].Step(+1);
            sb.AppendLine(Line("Menu + loja + editor de avatar (30 trocas)", before));

            // Mesa com 15 jogadores (avatares de todos).
            var logic = new GameObject("Partida");
            created.Add(logic);
            var controller = logic.AddComponent<LocalMatchController>();
            var screen = UiKit.Rect("Tela", canvasGo.transform).Fill().gameObject.AddComponent<MatchScreen>();
            controller.StartMatch(new OfflineMatchConfig { BotCount = 14, Seed = 5 }, PacingSettings.Instant);
            screen.Bind(controller);
            MatchDriver.AdvanceUntil(controller, v => v.Phase == Fodinha.Core.MatchPhase.Playing, humanActs: true);
            sb.AppendLine(Line("+ partida com 15 jogadores", before));

            GC.Collect();
            long monoAfter = GC.GetTotalMemory(true);
            sb.AppendLine("Objetos C# do jogo (heap): " + Mb(monoAfter - monoBefore));
            sb.AppendLine("Avatares no cache: " + AvatarPainter.CachedCount + " (limite " + AvatarPainter.MaxCached + ")");

            foreach (var o in created) UnityEngine.Object.DestroyImmediate(o);
            return sb.ToString();
        }

        private static string Line(string label, HashSet<int> before)
        {
            var mine = Resources.FindObjectsOfTypeAll<Texture>().Where(t => !before.Contains(t.GetInstanceID())).ToList();
            long bytes = mine.Sum(t => Profiler.GetRuntimeMemorySizeLong(t));
            var biggest = mine.OrderByDescending(t => Profiler.GetRuntimeMemorySizeLong(t)).Take(5)
                .Select(t => (string.IsNullOrEmpty(t.name) ? "(sem nome)" : t.name) + " " + t.width + "x" + t.height + " " + Mb(Profiler.GetRuntimeMemorySizeLong(t)));
            return label + ": " + mine.Count + " texturas, " + Mb(bytes) + "  | maiores: " + string.Join("; ", biggest);
        }

        private static string Mb(long b) => (b / (1024.0 * 1024.0)).ToString("0.0") + " MB";
    }
}
