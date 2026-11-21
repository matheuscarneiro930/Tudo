using System;
using System.IO;
using Fodinha.Game.UI;
using Fodinha.Hosting;
using UnityEditor;
using UnityEngine;

namespace Fodinha.EditorTools
{
    /// <summary>
    /// Folha PNG com vários avatares lado a lado (para revisar o desenho sem abrir o jogo).
    /// Batchmode: -executeMethod Fodinha.EditorTools.AvatarSheet.ExportFromCommandLine -sheetPath &lt;arquivo.png&gt;
    /// </summary>
    public static class AvatarSheet
    {
        [MenuItem("Fodinha/Exportar folha de avatares")]
        public static void ExportFromMenu()
        {
            string path = Path.GetFullPath("Temp/avatares.png");
            Export(path);
            EditorUtility.RevealInFinder(path);
        }

        public static void ExportFromCommandLine()
        {
            string path = Path.GetFullPath("Temp/avatares.png");
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-sheetPath") path = args[i + 1];
            Export(path);
        }

        public static void Export(string path)
        {
            const int cell = 256, cols = 6, rows = 4;
            var sheet = new Texture2D(cell * cols, cell * rows, TextureFormat.RGBA32, false);
            var fill = new Color32[sheet.width * sheet.height];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(30, 60, 45, 255);
            sheet.SetPixels32(fill);

            for (int i = 0; i < cols * rows; i++)
            {
                var spec = SpecFor(i);
                var tex = AvatarPainter.Paint(spec, cell, true);
                var px = tex.GetPixels32();
                int ox = (i % cols) * cell, oy = (rows - 1 - i / cols) * cell;
                for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                {
                    var src = px[y * cell + x];
                    if (src.a == 0) continue;
                    var dst = sheet.GetPixel(ox + x, oy + y);
                    float a = src.a / 255f;
                    sheet.SetPixel(ox + x, oy + y, Color.Lerp(dst, (Color)src, a));
                }
                UnityEngine.Object.DestroyImmediate(tex);
            }
            sheet.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
            Debug.Log("[Fodinha] Folha de avatares: " + path);
        }

        /// <summary>Os 8 cabelos, as 4 barbas, os 5 olhos e as 5 bocas aparecem pelo menos uma vez.</summary>
        private static AvatarSpec SpecFor(int i)
        {
            var s = new AvatarSpec();
            s[AvatarSpec.Part.Skin] = i % 6;
            s[AvatarSpec.Part.Hair] = i % AvatarSpec.OptionCount(AvatarSpec.Part.Hair);
            s[AvatarSpec.Part.Earrings] = (i / 3) % AvatarSpec.OptionCount(AvatarSpec.Part.Earrings);
            s[AvatarSpec.Part.HairColor] = (i * 3) % 8;
            s[AvatarSpec.Part.Eyes] = i % 5;
            s[AvatarSpec.Part.EyeColor] = i % 4;
            s[AvatarSpec.Part.Mouth] = (i / 2) % 5;
            s[AvatarSpec.Part.Beard] = i >= 8 && i < 16 ? (i % 4) : 0;
            s[AvatarSpec.Part.Shirt] = (i * 5) % 8;
            s[AvatarSpec.Part.Background] = (i * 7) % 8;
            return s;
        }
    }
}
