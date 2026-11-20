using System.IO;
using Fodinha.Core;
using Fodinha.Game.UI;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.UI;

namespace Fodinha.EditorTools
{
    /// <summary>
    /// Gera o ícone do app por código (três manilhas em leque sobre o feltro) e aplica no projeto:
    /// ícone padrão e, no Android, ícone adaptativo (fundo + frente) e redondo.
    ///   -executeMethod Fodinha.EditorTools.IconBuilder.Build   (precisa de gráficos: sem -nographics)
    /// </summary>
    public static class IconBuilder
    {
        public const string Folder = "Assets/Fodinha/Art";
        public const string IconPath = Folder + "/Icone.png";
        public const string BackgroundPath = Folder + "/IconeFundo.png";
        public const string ForegroundPath = Folder + "/IconeFrente.png";

        [MenuItem("Fodinha/Gerar ícone do app")]
        public static void Build()
        {
            Directory.CreateDirectory(Folder);
            // Quadrado e sem transparência: Android/iOS aplicam a própria máscara (círculo, cantos arredondados).
            UiSnapshot.RenderToPng(IconPath, 1024, 1024, root => { Background(root, false); Cards(root, 0.52f); });
            UiSnapshot.RenderToPng(BackgroundPath, 432, 432, root => Background(root, false));
            UiSnapshot.RenderToPng(ForegroundPath, 432, 432, root => Cards(root, 0.36f), transparent: true);
            AssetDatabase.Refresh();
            foreach (var p in new[] { IconPath, BackgroundPath, ForegroundPath }) ConfigureImporter(p);
            Apply();
            Debug.Log("[Fodinha] Ícone gerado e aplicado.");
        }

        /// <summary>Aplica os ícones já gerados (chamado também pelo ProjectConfigurator).</summary>
        public static void Apply()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            var bg = AssetDatabase.LoadAssetAtPath<Texture2D>(BackgroundPath);
            var fg = AssetDatabase.LoadAssetAtPath<Texture2D>(ForegroundPath);
            if (icon == null) return;

            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);

            SetAndroid(AndroidPlatformIconKind.Legacy, i => i.SetTexture(icon));
            SetAndroid(AndroidPlatformIconKind.Round, i => i.SetTexture(icon));
            if (bg != null && fg != null) SetAndroid(AndroidPlatformIconKind.Adaptive, i => i.SetTextures(bg, fg));
        }

        private static void SetAndroid(PlatformIconKind kind, System.Action<PlatformIcon> set)
        {
            var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
            foreach (var i in icons) set(i);
            PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
        }

        private static void ConfigureImporter(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter imp)) return;
            imp.textureType = TextureImporterType.Default;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }

        // ------------------------------------------------------------------ desenho

        private static void Background(RectTransform root, bool rounded)
        {
            var felt = UiKit.Image("Feltro", root, Palette.FeltEdge, rounded ? SpriteFactory.RoundedRect : null, 0.07f);
            felt.rectTransform.Fill();
            var glow = UiKit.Image("Luz", root, new Color(0.45f, 0.85f, 0.55f, 0.55f), SpriteFactory.SoftGlow);
            glow.rectTransform.Anchor(-0.25f, -0.15f, 1.25f, 1.25f);
        }

        /// <summary>Leque com 7♥, 4♣ (zap) e A♠, ocupando <paramref name="heightFraction"/> da altura.</summary>
        private static void Cards(RectTransform root, float heightFraction)
        {
            var area = UiKit.Rect("Cartas", root).Fill();
            string[] cards = { "7C", "AE", "4P" };
            float[] angles = { 22f, -22f, 0f };
            float[] offsets = { -0.2f, 0.2f, 0f };
            var size = root.rect.size;
            float h = size.y * heightFraction;
            for (int i = 0; i < cards.Length; i++)
            {
                var cv = CardView.Create(area, "Carta" + i);
                cv.AllowLargeText(3f);
                cv.Rect.Place(new Vector2(0.5f + offsets[i], 0.47f + (i == 2 ? 0.03f : 0f)), new Vector2(h * CardView.Aspect, h));
                cv.Rect.localRotation = Quaternion.Euler(0f, 0f, angles[i]);
                cv.ShowFace(Card.Parse(cards[i]), false);
                cv.SetInteractable(false);
                // contorno dourado na carta do meio (o zap)
                cv.SetHighlight(i == 2, Palette.Accent);
            }
        }
    }
}
