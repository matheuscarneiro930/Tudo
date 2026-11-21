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
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> tables = new System.Collections.Generic.Dictionary<string, Sprite>();
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> cardBacks = new System.Collections.Generic.Dictionary<string, Sprite>();
        private static Sprite background;
        private static Sprite softGlow;

        /// <summary>Retângulo branco com cantos arredondados (raio 24 px), fatiado em 9 partes.</summary>
        public static Sprite RoundedRect => roundedRect != null ? roundedRect : (roundedRect = BuildRounded(64, 24, 0f));

        /// <summary>Contorno arredondado (para destacar a vez/seleção).</summary>
        public static Sprite RoundedOutline => roundedOutline != null ? roundedOutline : (roundedOutline = BuildRounded(64, 24, 6f));

        public static Sprite Circle => circle != null ? circle : (circle = BuildCircle(128));

        /// <summary>Mesa escolhida pelo jogador (loja).</summary>
        public static Sprite Table => TableFor(GamePrefs.TableTheme);

        /// <summary>Verso das cartas do baralho escolhido pelo jogador (loja).</summary>
        public static Sprite CardBack => CardBackFor(GamePrefs.DeckTheme);

        public static Sprite TableFor(string id)
        {
            id ??= "";
            if (tables.TryGetValue(id, out var s) && s != null) return s;
            return tables[id] = BuildTable(512, CosmeticLooks.Table(id));
        }

        public static Sprite CardBackFor(string id)
        {
            id ??= "";
            if (cardBacks.TryGetValue(id, out var s) && s != null) return s;
            return cardBacks[id] = BuildCardBack(138, 200, CosmeticLooks.Deck(id));
        }

        public static Sprite Background => background != null ? background : (background = BuildVerticalGradient(Palette.BackgroundTop, Palette.BackgroundBottom));

        public static Sprite SoftGlow => softGlow != null ? softGlow : (softGlow = BuildGlow(128));

        private static readonly Sprite[] suits = new Sprite[4];

        /// <summary>
        /// Naipe desenhado (branco, para tingir de vermelho/preto). Não depende de fonte: os símbolos ♥♦♠♣
        /// não existem na fonte do navegador/celular e apareciam em branco.
        /// </summary>
        public static Sprite Suit(Core.Suit suit)
        {
            int i = (int)suit;
            if (suits[i] == null) suits[i] = BuildSuit(suit, 128);
            return suits[i];
        }

        /// <summary>Verdadeiro se o ponto (x, y em -0.5..0.5, y para cima) está dentro do naipe.</summary>
        public static bool InsideSuit(Core.Suit suit, float x, float y)
        {
            switch (suit)
            {
                case Core.Suit.Diamonds:
                    return Mathf.Abs(x) / 0.36f + Mathf.Abs(y) / 0.48f <= 1f;
                case Core.Suit.Hearts:
                    return InCircle(x, y, -0.2f, 0.17f, 0.23f) || InCircle(x, y, 0.2f, 0.17f, 0.23f) ||
                           InTriangle(x, y, -0.425f, 0.12f, 0.425f, 0.12f, 0f, -0.45f);
                case Core.Suit.Spades:
                    return InCircle(x, y, -0.2f, -0.06f, 0.21f) || InCircle(x, y, 0.2f, -0.06f, 0.21f) ||
                           InTriangle(x, y, -0.405f, -0.1f, 0.405f, -0.1f, 0f, 0.46f) ||
                           InTriangle(x, y, 0f, -0.05f, -0.15f, -0.46f, 0.15f, -0.46f);
                case Core.Suit.Clubs:
                    return InCircle(x, y, 0f, 0.22f, 0.19f) || InCircle(x, y, -0.22f, -0.08f, 0.19f) ||
                           InCircle(x, y, 0.22f, -0.08f, 0.19f) || InCircle(x, y, 0f, 0.02f, 0.12f) ||
                           InTriangle(x, y, 0f, 0.0f, -0.15f, -0.46f, 0.15f, -0.46f);
                default:
                    return false;
            }
        }

        private static bool InCircle(float x, float y, float cx, float cy, float r)
        {
            float dx = x - cx, dy = y - cy;
            return dx * dx + dy * dy <= r * r;
        }

        private static bool InTriangle(float px, float py, float ax, float ay, float bx, float by, float cx, float cy)
        {
            float d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by);
            float d2 = (px - cx) * (by - cy) - (bx - cx) * (py - cy);
            float d3 = (px - ax) * (cy - ay) - (cx - ax) * (py - ay);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0;
            bool pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        private static Sprite BuildSuit(Core.Suit suit, int size)
        {
            var tex = NewTexture(size, size);
            var pixels = new Color32[size * size];
            const int samples = 4; // 4x4 amostras por pixel = bordas suaves
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < samples; sy++)
                for (int sx = 0; sx < samples; sx++)
                {
                    float u = (x + (sx + 0.5f) / samples) / size - 0.5f;
                    float v = (y + (sy + 0.5f) / samples) / size - 0.5f;
                    if (InsideSuit(suit, u, v)) hits++;
                }
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * hits / (samples * samples)));
            }
            tex.SetPixels32(pixels);
            return Finish(tex, Vector4.zero);
        }

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

        /// <summary>Mesa oval: feltro com degradê radial e borda (madeira ou dourada), nas cores do item da loja.</summary>
        private static Sprite BuildTable(int size, CosmeticLooks.TableLook look)
        {
            var tex = NewTexture(size, size);
            var pixels = new Color32[size * size];
            float r = size * 0.5f;
            float rimWidth = size * 0.045f;
            Color rimDark = look.Rim * 0.7f; rimDark.a = 1f;
            Color rimLight = Color.Lerp(look.Rim, Color.white, 0.35f);
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
                    c = Color.Lerp(look.Rim, rimDark, t);
                    // borda metálica: faixa de brilho
                    if (look.ShinyRim) c = Color.Lerp(c, rimLight, Mathf.Clamp01(1f - Mathf.Abs(t - 0.35f) / 0.2f) * 0.7f);
                }
                else
                {
                    float t = dist / (r - rimWidth);
                    c = Color.Lerp(look.Center, look.Edge, t * t);
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

        /// <summary>Verso da carta: fundo colorido com padrão (losangos, listras, bolinhas ou xadrez), moldura e cantos arredondados.</summary>
        private static Sprite BuildCardBack(int w, int h, CosmeticLooks.DeckLook look)
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
                if (d > -frame * 0.55f) c = look.Frame;                    // moldura
                else if (d > -frame) c = look.Back * 0.8f;                 // filete escuro
                else c = CosmeticLooks.InsidePattern(look.Pattern, px, py) ? look.PatternColor : look.Back;
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
