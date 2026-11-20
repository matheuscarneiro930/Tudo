using UnityEngine;

namespace Fodinha.Game.UI
{
    /// <summary>
    /// Sprites gerados por código (sem assets externos): retângulo arredondado (9-slice), contorno,
    /// círculo, mesa oval com feltro, verso de carta e degradê de fundo. Criados uma vez e reaproveitados.
    /// </summary>
    public static class SpriteFactory
    {
        private const float Ppu = 100f;

        private static Sprite roundedRect;
        private static Sprite roundedOutline;
        private static Sprite circle;
        private static Sprite table;
        private static Sprite cardBack;
        private static Sprite background;
        private static Sprite softGlow;

        /// <summary>Retângulo branco com cantos arredondados (raio 24 px), fatiado em 9 partes.</summary>
        public static Sprite RoundedRect => roundedRect != null ? roundedRect : (roundedRect = BuildRounded(64, 24, 0f));

        /// <summary>Contorno arredondado (para destacar a vez/seleção).</summary>
        public static Sprite RoundedOutline => roundedOutline != null ? roundedOutline : (roundedOutline = BuildRounded(64, 24, 6f));

        public static Sprite Circle => circle != null ? circle : (circle = BuildCircle(128));

        public static Sprite Table => table != null ? table : (table = BuildTable(512));

        public static Sprite CardBack => cardBack != null ? cardBack : (cardBack = BuildCardBack(138, 200));

        public static Sprite Background => background != null ? background : (background = BuildVerticalGradient(Palette.BackgroundTop, Palette.BackgroundBottom));

        public static Sprite SoftGlow => softGlow != null ? softGlow : (softGlow = BuildGlow(128));

        private static Texture2D NewTexture(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            return tex;
        }

        private static Sprite Finish(Texture2D tex, Vector4 border)
        {
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), Ppu, 0,
                                       SpriteMeshType.FullRect, border);
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        /// <summary>Distância (com sinal) até a borda de um retângulo arredondado. Negativo = dentro.</summary>
        private static float RoundedRectDistance(float x, float y, float w, float h, float r)
        {
            float qx = Mathf.Abs(x - w * 0.5f) - (w * 0.5f - r);
            float qy = Mathf.Abs(y - h * 0.5f) - (h * 0.5f - r);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        private static Sprite BuildRounded(int size, int radius, float stroke)
        {
            var tex = NewTexture(size, size);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = RoundedRectDistance(x + 0.5f, y + 0.5f, size, size, radius);
                    float alpha = Mathf.Clamp01(0.5f - d);
                    if (stroke > 0f) alpha *= Mathf.Clamp01(d + stroke + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            tex.SetPixels32(pixels);
            return Finish(tex, new Vector4(radius, radius, radius, radius));
        }

        private static Sprite BuildCircle(int size)
        {
            var tex = NewTexture(size, size);
            var pixels = new Color32[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                float alpha = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
            tex.SetPixels32(pixels);
            return Finish(tex, Vector4.zero);
        }

        private static Sprite BuildGlow(int size)
        {
            var tex = NewTexture(size, size);
            var pixels = new Color32[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                float t = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / r);
                float alpha = (1f - t) * (1f - t);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
            tex.SetPixels32(pixels);
            return Finish(tex, Vector4.zero);
        }

        /// <summary>Mesa oval: feltro verde com degradê radial e borda de madeira.</summary>
        private static Sprite BuildTable(int size)
        {
            var tex = NewTexture(size, size);
            var pixels = new Color32[size * size];
            float r = size * 0.5f;
            float rimWidth = size * 0.045f;
            Color rimDark = Palette.Rim * 0.7f; rimDark.a = 1f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float edgeAlpha = Mathf.Clamp01(r - dist);
                Color c;
                if (dist > r - rimWidth)
                {
                    float t = (dist - (r - rimWidth)) / rimWidth;
                    c = Color.Lerp(Palette.Rim, rimDark, t);
                }
                else
                {
                    float t = dist / (r - rimWidth);
                    c = Color.Lerp(Palette.FeltCenter, Palette.FeltEdge, t * t);
                    // sombra interna perto da borda
                    float shade = Mathf.Clamp01((t - 0.93f) / 0.07f);
                    c = Color.Lerp(c, c * 0.65f, shade);
                }
                c.a = edgeAlpha;
                pixels[y * size + x] = c;
            }
            tex.SetPixels32(pixels);
            return Finish(tex, Vector4.zero);
        }

        /// <summary>Verso da carta: fundo vinho com losangos, moldura clara e cantos arredondados.</summary>
        private static Sprite BuildCardBack(int w, int h)
        {
            var tex = NewTexture(w, h);
            var pixels = new Color32[w * h];
            float radius = w * 0.09f;
            float frame = w * 0.07f;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float d = RoundedRectDistance(px, py, w, h, radius);
                float alpha = Mathf.Clamp01(0.5f - d);
                Color c;
                if (d > -frame * 0.55f) c = Palette.CardFace;            // moldura branca
                else if (d > -frame) c = Palette.CardBack * 0.8f;          // filete escuro
                else
                {
                    // padrão de losangos
                    float u = (px + py) / 14f, v = (px - py) / 14f;
                    float fu = Mathf.Abs(u - Mathf.Round(u)), fv = Mathf.Abs(v - Mathf.Round(v));
                    bool line = fu < 0.12f || fv < 0.12f;
                    c = line ? Palette.CardBackPattern : Palette.CardBack;
                }
                c.a = alpha;
                pixels[y * w + x] = c;
            }
            tex.SetPixels32(pixels);
            return Finish(tex, Vector4.zero);
        }

        private static Sprite BuildVerticalGradient(Color top, Color bottom)
        {
            const int h = 256;
            var tex = NewTexture(4, h);
            var pixels = new Color32[4 * h];
            for (int y = 0; y < h; y++)
            {
                Color c = Color.Lerp(bottom, top, y / (float)(h - 1));
                for (int x = 0; x < 4; x++) pixels[y * 4 + x] = c;
            }
            tex.SetPixels32(pixels);
            return Finish(tex, Vector4.zero);
        }
    }
}
