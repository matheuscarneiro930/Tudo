using System;
using UnityEngine;
using UnityEngine.UI;

namespace Fodinha.Game.UI
{
    /// <summary>Cores do jogo (um só lugar para ajustar o visual).</summary>
    public static class Palette
    {
        public static readonly Color BackgroundTop = Hex("0E4632");
        public static readonly Color BackgroundBottom = Hex("06261C");
        public static readonly Color FeltCenter = Hex("238A55");
        public static readonly Color FeltEdge = Hex("146038");
        public static readonly Color Rim = Hex("5B3418");
        public static readonly Color Panel = new Color(0f, 0f, 0f, 0.45f);
        public static readonly Color PanelStrong = new Color(0.04f, 0.09f, 0.07f, 0.94f);
        public static readonly Color Accent = Hex("FFC83D");
        public static readonly Color AccentDark = Hex("3A2A00");
        public static readonly Color Text = Color.white;
        public static readonly Color TextMuted = new Color(1f, 1f, 1f, 0.65f);
        public static readonly Color Heart = Hex("FF4D4D");
        public static readonly Color RedSuit = Hex("D32F2F");
        public static readonly Color BlackSuit = Hex("1C1C1C");
        public static readonly Color CardFace = Hex("FBFAF5");
        public static readonly Color CardBack = Hex("8E1B2B");
        public static readonly Color CardBackPattern = Hex("B8323F");
        public static readonly Color Manilha = Hex("FFB300");
        public static readonly Color Good = Hex("4CD964");
        public static readonly Color Bad = Hex("FF5A5A");
        public static readonly Color Button = Hex("FFC83D");
        public static readonly Color ButtonText = Hex("2B1E00");
        public static readonly Color ButtonSecondary = Hex("2F5D4A");
        public static readonly Color Disabled = new Color(0.35f, 0.35f, 0.35f, 0.8f);

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }

        public static string ToHex(Color c) => ColorUtility.ToHtmlStringRGBA(c);
    }

    /// <summary>Atalhos para montar a interface por código (sem prefabs nem assets externos).</summary>
    public static class UiKit
    {
        public const int UiLayer = 5;
        private static Font font;

        public static Font Font
        {
            get
            {
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UiLayer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>Posiciona por âncoras relativas ao pai (0..1), sem margens.</summary>
        public static RectTransform Anchor(this RectTransform rt, float xMin, float yMin, float xMax, float yMax)
        {
            rt.anchorMin = new Vector2(xMin, yMin);
            rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            return rt;
        }

        /// <summary>Posiciona com tamanho fixo centralizado num ponto relativo do pai.</summary>
        public static RectTransform Place(this RectTransform rt, Vector2 anchor, Vector2 size, Vector2 offset = default)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = offset;
            return rt;
        }

        public static RectTransform Fill(this RectTransform rt) => rt.Anchor(0f, 0f, 1f, 1f);

        public static Image Image(string name, Transform parent, Color color, Sprite sprite = null, float cornerScale = 1f)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            if (sprite != null)
            {
                img.sprite = sprite;
                if (sprite.border != Vector4.zero)
                {
                    img.type = UnityEngine.UI.Image.Type.Sliced;
                    img.pixelsPerUnitMultiplier = cornerScale;
                }
            }
            return img;
        }

        public static Text Label(string name, Transform parent, string text, int size, Color color,
                                 TextAnchor anchor = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal, bool bestFit = false)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.fontStyle = style;
            t.raycastTarget = false;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            if (bestFit)
            {
                t.resizeTextForBestFit = true;
                t.resizeTextMinSize = 8;
                t.resizeTextMaxSize = size;
            }
            return t;
        }

        public static Button Button(string name, Transform parent, string label, Action onClick, Color? background = null,
                                    Color? textColor = null, int fontSize = 40)
        {
            var img = Image(name, parent, background ?? Palette.Button, SpriteFactory.RoundedRect, 1.5f);
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.95f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
            button.colors = colors;

            var text = Label("Texto", img.transform, label, fontSize, textColor ?? Palette.ButtonText, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            text.rectTransform.Fill();
            text.rectTransform.offsetMin = new Vector2(8f, 4f);
            text.rectTransform.offsetMax = new Vector2(-8f, -4f);

            button.onClick.AddListener(() => Audio.Sfx.Play(Audio.Sound.Click, 0.6f));
            if (onClick != null) button.onClick.AddListener(() => onClick());
            return button;
        }

        public static Text ButtonLabel(Button button) => button.GetComponentInChildren<Text>(true);

        /// <summary>Corações de vida em texto rico: cheios em vermelho, perdidos apagados.</summary>
        public static string Hearts(int lives, int maxLives)
        {
            if (lives < 0) lives = 0;
            int lost = Mathf.Max(0, maxLives - lives);
            var full = new string('♥', lives);
            var empty = new string('♥', lost);
            return (lives > 0 ? "<color=#" + Palette.ToHex(Palette.Heart) + ">" + full + "</color>" : "") +
                   (lost > 0 ? "<color=#FFFFFF33>" + empty + "</color>" : "");
        }

        public static string Colored(string text, Color color) => "<color=#" + Palette.ToHex(color) + ">" + text + "</color>";

        /// <summary>Destroy no jogo, DestroyImmediate no Editor parado (testes).</summary>
        public static void SafeDestroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }

        public static void DestroyChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var child = t.GetChild(i).gameObject;
                if (Application.isPlaying) UnityEngine.Object.Destroy(child);
                else UnityEngine.Object.DestroyImmediate(child);
            }
        }
    }
}
