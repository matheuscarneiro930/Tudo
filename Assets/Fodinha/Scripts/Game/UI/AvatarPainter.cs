using System.Collections.Generic;
using Fodinha.Hosting;
using UnityEngine;

namespace Fodinha.Game.UI
{
    /// <summary>
    /// Desenha o avatar (estilo "boneco 3D" de celular: cabeça grande, olhos expressivos, sombras suaves) num círculo,
    /// tudo por código a partir de um <see cref="AvatarSpec"/>. As imagens ficam guardadas para não desenhar de novo.
    /// Coordenadas: x e y de -1 a 1 (y para cima), centro do círculo em (0, 0).
    /// </summary>
    public static class AvatarPainter
    {
        public const int TextureSize = 256;
        private const int MaxCached = 64;

        private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        private static readonly Queue<string> order = new Queue<string>();

        public static int CachedCount => cache.Count;

        // ------------------------------------------------------------------ paletas (índice = opção do AvatarSpec)

        private static readonly Color[] Skins = { C("FBDCC8"), C("F2C5A0"), C("E0A879"), C("C68650"), C("8D5730"), C("5E3A22") };
        private static readonly Color[] HairColors = { C("23201E"), C("4A2C1A"), C("7B4A2A"), C("E0B866"), C("B9502A"), C("B8B8B8"), C("3E7BD6"), C("E46BA6") };
        private static readonly Color[] EyeColors = { C("6B4226"), C("2A1E18"), C("3F8C4A"), C("3D7CC9") };
        private static readonly Color[] Shirts = { C("2F6FD1"), C("D63B3B"), C("2E9E57"), C("F2C230"), C("7E4AC2"), C("2A2A2E"), C("F2F2F2"), C("F07B2A") };
        private static readonly Color[] Backgrounds = { C("7FB7F2"), C("8ED9A2"), C("F5A9C8"), C("F7DC7A"), C("B9A3EE"), C("F7B27E"), C("C9CED6"), C("7FD9D2") };

        private static Color C(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }

        /// <summary>Sprite do avatar (texto do AvatarSpec). Texto inválido = avatar sorteado pela semente.</summary>
        public static Sprite Get(string encoded, string fallbackSeed = "")
        {
            var spec = AvatarSpec.ParseOrSeed(encoded, fallbackSeed);
            return Get(spec);
        }

        public static Sprite Get(AvatarSpec spec)
        {
            string key = spec.Encode();
            if (cache.TryGetValue(key, out var sprite) && sprite != null) return sprite;

            var tex = Paint(spec, TextureSize);
            sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = "Avatar_" + key;
            sprite.hideFlags = HideFlags.DontSave;
            cache[key] = sprite;
            order.Enqueue(key);
            while (order.Count > MaxCached)
            {
                var old = order.Dequeue();
                if (old == key || !cache.TryGetValue(old, out var s)) continue;
                cache.Remove(old);
                if (s != null)
                {
                    Object.DestroyImmediate(s.texture);
                    Object.DestroyImmediate(s);
                }
            }
            return sprite;
        }

        // ------------------------------------------------------------------ desenho

        /// <summary>Desenha o avatar numa textura quadrada (com mipmaps: fica nítido também pequeno).</summary>
        public static Texture2D Paint(AvatarSpec spec, int size, bool keepReadable = false)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                hideFlags = HideFlags.DontSave,
                name = "Avatar"
            };
            var pixels = new Color32[size * size];
            var ctx = new Ctx(spec, 2.4f / size);
            for (int py = 0; py < size; py++)
            {
                float y = (py + 0.5f) * 2f / size - 1f; // linha 0 da textura é a de baixo
                for (int px = 0; px < size; px++)
                {
                    float x = (px + 0.5f) * 2f / size - 1f;
                    pixels[py * size + px] = ctx.Shade(x, y);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, !keepReadable);
            return tex;
        }

        /// <summary>Tudo que é preciso para pintar um pixel. Cada camada mistura sua cor por cima da anterior.</summary>
        private sealed class Ctx
        {
            private readonly float aa;
            private readonly int hair, eyes, mouth, beard;
            private readonly Color skin, skinDark, skinLight, hairColor, hairDark, hairLight, iris, shirt, bg;

            // cabeça
            private const float HeadX = 0f, HeadY = 0.06f, HeadRx = 0.47f, HeadRy = 0.56f;
            private const float EyeY = 0.04f, EyeX = 0.185f;
            private const float MouthY = -0.27f;

            public Ctx(AvatarSpec s, float aa)
            {
                this.aa = aa;
                hair = s[AvatarSpec.Part.Hair];
                eyes = s[AvatarSpec.Part.Eyes];
                mouth = s[AvatarSpec.Part.Mouth];
                beard = s[AvatarSpec.Part.Beard];
                skin = Skins[s[AvatarSpec.Part.Skin]];
                skinDark = Mul(skin, 0.78f);
                skinLight = Color.Lerp(skin, Color.white, 0.22f);
                hairColor = HairColors[s[AvatarSpec.Part.HairColor]];
                hairDark = Mul(hairColor, 0.62f);
                hairLight = Color.Lerp(hairColor, Color.white, 0.28f);
                iris = EyeColors[s[AvatarSpec.Part.EyeColor]];
                shirt = Shirts[s[AvatarSpec.Part.Shirt]];
                bg = Backgrounds[s[AvatarSpec.Part.Background]];
            }

            private static Color Mul(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

            /// <summary>Cobertura (0..1) a partir da distância com sinal (negativa = dentro), com borda suave.</summary>
            private float Cov(float d) => Mathf.Clamp01(0.5f - d / aa);

            private static float Ellipse(float x, float y, float cx, float cy, float rx, float ry)
            {
                float dx = (x - cx) / rx, dy = (y - cy) / ry;
                return (Mathf.Sqrt(dx * dx + dy * dy) - 1f) * Mathf.Min(rx, ry);
            }

            private static float Circle(float x, float y, float cx, float cy, float r)
            {
                float dx = x - cx, dy = y - cy;
                return Mathf.Sqrt(dx * dx + dy * dy) - r;
            }

            /// <summary>Segmento com pontas redondas (sobrancelha, boca séria...).</summary>
            private static float Capsule(float x, float y, float ax, float ay, float bx, float by, float r)
            {
                float pax = x - ax, pay = y - ay, bax = bx - ax, bay = by - ay;
                float h = Mathf.Clamp01((pax * bax + pay * bay) / (bax * bax + bay * bay));
                float dx = pax - bax * h, dy = pay - bay * h;
                return Mathf.Sqrt(dx * dx + dy * dy) - r;
            }

            private static float Box(float x, float y, float cx, float cy, float hw, float hh, float r)
            {
                float qx = Mathf.Abs(x - cx) - hw + r, qy = Mathf.Abs(y - cy) - hh + r;
                float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
                return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
            }

            private static float Union(float a, float b) => Mathf.Min(a, b);
            private static float Intersect(float a, float b) => Mathf.Max(a, b);
            private static float Subtract(float a, float b) => Mathf.Max(a, -b);

            /// <summary>Arco (traço) de uma circunferência, só a parte abaixo/acima de um limite em y.</summary>
            private static float ArcStroke(float x, float y, float cx, float cy, float r, float half, float yLimit, bool below)
            {
                float ring = Mathf.Abs(Circle(x, y, cx, cy, r)) - half;
                float cut = below ? y - yLimit : yLimit - y;
                return Mathf.Max(ring, cut);
            }

            // Cabeça um pouco mais estreita no queixo (formato de ovo).
            private static float Head(float x, float y)
            {
                float t = Mathf.Clamp01((HeadY - y) / HeadRy);
                float k = 1f + 0.16f * t * t;
                return Ellipse(x * k, y, HeadX, HeadY, HeadRx, HeadRy);
            }

            public Color32 Shade(float x, float y)
            {
                float disc = Circle(x, y, 0f, 0f, 1f);
                float alpha = Cov(disc);
                if (alpha <= 0f) return new Color32(0, 0, 0, 0);

                // Fundo: degradê do centro para a borda.
                float r = Mathf.Sqrt(x * x + y * y);
                Color c = Color.Lerp(Color.Lerp(bg, Color.white, 0.25f), Mul(bg, 0.85f), r);

                // Cabelo de trás (longo/cacheado/coque solto).
                float back = HairBack(x, y);
                if (back < aa) c = Color.Lerp(c, HairShade(x, y, hairDark), Cov(back));

                // Ombros e camiseta.
                float body = Ellipse(x, y, 0f, -1.18f, 0.80f, 0.52f);
                if (body < aa)
                {
                    float t = Mathf.Clamp01((y + 0.66f) / 0.4f);
                    Color sh = Color.Lerp(Mul(shirt, 0.8f), shirt, t * 0.6f + 0.4f * Mathf.Clamp01(-x * 0.8f + 0.5f));
                    c = Color.Lerp(c, sh, Cov(body));
                    // gola
                    float collar = Intersect(Ellipse(x, y, 0f, -0.70f, 0.21f, 0.11f), y + 0.7f);
                    c = Color.Lerp(c, Mul(shirt, 0.65f), Cov(collar));
                }

                // Pescoço (com sombra sob o queixo).
                float neck = Box(x, y, 0f, -0.58f, 0.15f, 0.2f, 0.08f);
                if (neck < aa)
                {
                    float shadow = Mathf.Clamp01((y + 0.40f) / 0.12f);
                    c = Color.Lerp(c, Color.Lerp(skinDark, Mul(skinDark, 0.85f), shadow), Cov(neck));
                }

                // Orelhas.
                float ears = Union(Ellipse(x, y, -0.475f, 0.0f, 0.085f, 0.115f), Ellipse(x, y, 0.475f, 0.0f, 0.085f, 0.115f));
                if (ears < aa)
                {
                    c = Color.Lerp(c, skinDark, Cov(ears));
                    float inner = Union(Ellipse(x, y, -0.48f, 0.0f, 0.04f, 0.065f), Ellipse(x, y, 0.48f, 0.0f, 0.04f, 0.065f));
                    c = Color.Lerp(c, Mul(skinDark, 0.85f), Cov(inner) * 0.7f);
                }

                // Rosto: luz de cima-esquerda, sombra na borda.
                float head = Head(x, y);
                if (head < aa)
                {
                    float lx = (x - HeadX) / HeadRx, ly = (y - HeadY) / HeadRy;
                    float light = Mathf.Clamp01(0.55f - 0.25f * lx + 0.3f * ly);
                    Color face = Color.Lerp(skinDark, skinLight, light);
                    float rim = Mathf.Clamp01(1f + head / 0.09f);
                    face = Color.Lerp(face, Mul(skin, 0.82f), rim * 0.55f);
                    c = Color.Lerp(c, face, Cov(head));

                    // Bochechas rosadas.
                    float cheek = Mathf.Min(Glow(x, y, -0.27f, -0.13f, 0.11f), 1f) + Mathf.Min(Glow(x, y, 0.27f, -0.13f, 0.11f), 1f);
                    c = Color.Lerp(c, new Color(0.95f, 0.45f, 0.45f, 1f), cheek * 0.22f);
                }

                // Barba (por baixo da boca).
                if (beard > 0) c = DrawBeard(c, x, y, head);

                // Nariz.
                float nose = Ellipse(x, y, 0f, -0.1f, 0.06f, 0.045f);
                c = Color.Lerp(c, skinDark, Cov(nose) * 0.45f);
                c = Color.Lerp(c, skinLight, Cov(Circle(x, y, -0.018f, -0.085f, 0.018f)) * 0.6f);

                c = DrawEyes(c, x, y);
                c = DrawBrows(c, x, y);
                c = DrawMouth(c, x, y);
                if (beard == 2) c = DrawMustache(c, x, y);

                // Cabelo da frente: sombra suave na testa logo abaixo dele, depois o cabelo com borda mais escura.
                float front = HairFront(x, y);
                if (front > 0f && front < 0.06f && head < 0f)
                    c = Color.Lerp(c, Mul(c, 0.8f), (1f - front / 0.06f) * 0.6f);
                if (front < aa)
                {
                    Color h = HairShade(x, y, hairColor);
                    h = Color.Lerp(h, hairDark, Mathf.Clamp01(1f + front / 0.03f) * 0.55f);
                    c = Color.Lerp(c, h, Cov(front));
                }
                if (hair == 6) // moicano: laterais raspadas (só sobre a cabeça)
                {
                    float shaved = Subtract(Intersect(head, 0.2f - y), Box(x, y, 0f, 0.4f, 0.12f, 0.5f, 0.05f));
                    c = Color.Lerp(c, hairColor, Cov(shaved) * 0.14f);
                }

                // Brilho "plástico" no alto da cabeça.
                float shine = Glow(x, y, -0.17f, 0.42f, 0.13f);
                c = Color.Lerp(c, Color.white, Mathf.Min(shine, 1f) * 0.18f);

                // Contorno sutil do círculo.
                c = Color.Lerp(c, Mul(bg, 0.7f), Mathf.Clamp01(1f + disc / 0.03f) * 0.5f);

                c.a = alpha;
                return c;
            }

            /// <summary>Mancha suave (1 no centro, 0 no raio).</summary>
            private static float Glow(float x, float y, float cx, float cy, float radius)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / radius;
                return d >= 1f ? 0f : (1f - d * d) * (1f - d * d);
            }

            /// <summary>Cabelo com luz de cima-esquerda e fios sutis.</summary>
            private Color HairShade(float x, float y, Color baseColor)
            {
                float light = Mathf.Clamp01(0.5f - 0.35f * x + 0.5f * (y - 0.3f));
                Color col = Color.Lerp(Mul(baseColor, 0.72f), Color.Lerp(baseColor, hairLight, 0.5f), light);
                // Mechas largas e suaves (não "listras") e um brilho de cabelo liso.
                float angle = Mathf.Atan2(y - 0.05f, x);
                float strands = 0.975f + 0.025f * Mathf.Sin(angle * 18f + Mathf.Sin(y * 7f) * 1.5f);
                col = Mul(col, strands);
                float gloss = Mathf.Min(Glow(x, y, -0.16f, 0.5f, 0.24f), 1f);
                return Color.Lerp(col, Color.Lerp(hairLight, Color.white, 0.2f), gloss * 0.35f);
            }

            private float HairBack(float x, float y)
            {
                switch (hair)
                {
                    case 3: // cacheado: volume em volta
                    {
                        float d = Ellipse(x, y, 0f, 0.14f, 0.62f, 0.6f);
                        for (int i = 0; i < 7; i++)
                        {
                            float a = Mathf.Deg2Rad * (-30f + i * 40f);
                            d = Union(d, Circle(x, y, Mathf.Cos(a) * 0.56f, 0.12f + Mathf.Sin(a) * 0.52f, 0.17f));
                        }
                        return Intersect(d, -0.42f - y);
                    }
                    case 4: // longo: cai até os ombros
                        return Union(Ellipse(x, y, 0f, 0.12f, 0.6f, 0.62f), Box(x, y, 0f, -0.3f, 0.6f, 0.42f, 0.18f));
                    case 7: // franja: um pouco de volume atrás
                        return Ellipse(x, y, 0f, 0.1f, 0.56f, 0.6f);
                    default:
                        return 10f;
                }
            }

            private float HairFront(float x, float y)
            {
                // "Touca" que cobre o alto da cabeça, recortada por uma linha do cabelo.
                float cap = Ellipse(x, y, 0f, HeadY + 0.03f, HeadRx + 0.035f, HeadRy + 0.04f);
                float nx = x / 0.5f;
                switch (hair)
                {
                    case 1: // curto
                    {
                        float line = 0.27f + 0.07f * nx * nx + 0.018f * Mathf.Sin(x * 26f);
                        float d = Intersect(cap, line - y);
                        float burns = Union(Box(x, y, -0.445f, 0.2f, 0.04f, 0.17f, 0.03f), Box(x, y, 0.445f, 0.2f, 0.04f, 0.17f, 0.03f));
                        return Union(d, Intersect(burns, cap));
                    }
                    case 2: // topete
                    {
                        float line = 0.28f + 0.07f * nx * nx + 0.02f * Mathf.Sin(x * 20f + 1f);
                        float d = Intersect(cap, line - y);
                        d = Union(d, Ellipse(x, y, 0.06f, 0.6f, 0.36f, 0.16f));
                        d = Union(d, Ellipse(x, y, 0.2f, 0.66f, 0.2f, 0.11f));
                        float burns = Union(Box(x, y, -0.445f, 0.2f, 0.04f, 0.17f, 0.03f), Box(x, y, 0.445f, 0.2f, 0.04f, 0.17f, 0.03f));
                        return Union(d, Intersect(burns, cap));
                    }
                    case 3: // cacheado: bolinhas na testa e no alto
                    {
                        float d = Intersect(cap, 0.3f - 0.1f * nx * nx - y);
                        for (int i = 0; i < 9; i++)
                        {
                            float a = Mathf.Deg2Rad * (15f + i * 18.75f);
                            d = Union(d, Circle(x, y, Mathf.Cos(a) * 0.45f, 0.16f + Mathf.Sin(a) * 0.45f, 0.12f));
                        }
                        return d;
                    }
                    case 4: // longo, repartido de lado
                    {
                        float line = 0.36f - 0.2f * (nx + 0.35f) * (nx + 0.35f);
                        float d = Intersect(cap, line - y);
                        float sides = Union(Box(x, y, -0.5f, -0.22f, 0.1f, 0.48f, 0.08f), Box(x, y, 0.5f, -0.22f, 0.1f, 0.48f, 0.08f));
                        return Union(d, sides);
                    }
                    case 5: // coque
                    {
                        float line = 0.3f + 0.05f * nx * nx;
                        float d = Intersect(cap, line - y);
                        return Union(d, Circle(x, y, 0f, 0.74f, 0.17f));
                    }
                    case 6: // moicano
                        return Intersect(Box(x, y, 0f, 0.5f, 0.1f, 0.3f, 0.08f), Ellipse(x, y, 0f, 0.12f, 0.52f, 0.72f));
                    case 7: // franja reta com pontas
                    {
                        float line = 0.17f + 0.035f * Mathf.Abs(Mathf.Sin(x * 22f));
                        float d = Intersect(cap, line - y);
                        float sides = Union(Box(x, y, -0.47f, 0.02f, 0.07f, 0.22f, 0.06f), Box(x, y, 0.47f, 0.02f, 0.07f, 0.22f, 0.06f));
                        return Union(d, sides);
                    }
                    default: // careca
                        return 10f;
                }
            }

            private Color DrawEyes(Color c, float x, float y)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    float cx = side * EyeX;
                    int style = eyes == 4 && side > 0 ? 2 : eyes; // piscando: olho direito fechado
                    if (style == 2)
                    {
                        // Feliz: arco "^".
                        float arc = ArcStroke(x, y, cx, EyeY - 0.05f, 0.075f, 0.017f, EyeY - 0.045f, false);
                        c = Color.Lerp(c, C2(0.22f, 0.14f, 0.1f), Cov(arc));
                        continue;
                    }
                    float s = style == 1 ? 1.2f : 1f;
                    float rx = 0.1f * s, ry = 0.088f * s;
                    float sclera = Ellipse(x, y, cx, EyeY, rx, ry);
                    if (sclera > aa * 2f) continue;
                    float shade = Mathf.Clamp01((EyeY - y) / ry * 0.5f + 0.5f);
                    c = Color.Lerp(c, Color.Lerp(Color.white, new Color(0.86f, 0.88f, 0.92f), shade * 0.6f), Cov(sclera));

                    float lookX = cx + 0.012f;
                    float irisD = Intersect(Circle(x, y, lookX, EyeY - 0.005f, 0.06f * s), sclera);
                    float ir = Mathf.Clamp01(Circle(x, y, lookX, EyeY - 0.005f, 0.06f * s) / (-0.06f * s));
                    c = Color.Lerp(c, Color.Lerp(Mul(iris, 0.6f), Color.Lerp(iris, Color.white, 0.15f), ir), Cov(irisD));
                    c = Color.Lerp(c, C2(0.05f, 0.04f, 0.04f), Cov(Intersect(Circle(x, y, lookX, EyeY - 0.005f, 0.03f * s), sclera)));
                    c = Color.Lerp(c, Color.white, Cov(Circle(x, y, lookX + 0.022f * s, EyeY + 0.025f * s, 0.017f * s)));
                    c = Color.Lerp(c, Color.white, Cov(Circle(x, y, lookX - 0.02f * s, EyeY - 0.03f * s, 0.008f * s)) * 0.8f);

                    // Linha dos cílios em cima.
                    float lash = Intersect(Mathf.Abs(sclera) - 0.012f, (EyeY + ry * 0.15f) - y);
                    c = Color.Lerp(c, C2(0.12f, 0.08f, 0.07f), Cov(lash));

                    if (style == 3)
                    {
                        // Sonolento: pálpebra cobre metade de cima.
                        float lid = Intersect(Ellipse(x, y, cx, EyeY, rx + 0.012f, ry + 0.012f), (EyeY + 0.008f) - y);
                        c = Color.Lerp(c, skinDark, Cov(lid));
                        float lidLine = Intersect(Mathf.Abs(y - (EyeY + 0.008f)) - 0.01f, Ellipse(x, y, cx, EyeY, rx + 0.005f, ry));
                        c = Color.Lerp(c, C2(0.15f, 0.1f, 0.08f), Cov(lidLine));
                    }
                }
                return c;
            }

            private static Color C2(float r, float g, float b) => new Color(r, g, b, 1f);

            private Color DrawBrows(Color c, float x, float y)
            {
                float lift = eyes == 1 ? 0.03f : eyes == 3 ? -0.015f : 0f;
                Color brow = hair == 0 && beard == 0 ? Mul(skinDark, 0.7f) : Mul(hairColor, 0.85f);
                if (hairColor.r > 0.8f && hairColor.g > 0.6f) brow = Mul(hairColor, 0.7f); // loiro: sobrancelha mais visível
                for (int side = -1; side <= 1; side += 2)
                {
                    float ax = side * 0.1f, bx = side * 0.27f;
                    float ay = EyeY + 0.15f + lift, by = EyeY + 0.135f + lift + (eyes == 2 ? 0.015f : 0f);
                    float d = Capsule(x, y, ax, ay, bx, by, 0.022f);
                    c = Color.Lerp(c, brow, Cov(d));
                }
                return c;
            }

            private Color DrawMouth(Color c, float x, float y)
            {
                Color lip = new Color(0.55f, 0.2f, 0.2f, 1f);
                Color inside = new Color(0.36f, 0.08f, 0.1f, 1f);
                switch (mouth)
                {
                    case 0: // sorriso
                        return Color.Lerp(c, lip, Cov(ArcStroke(x, y, 0f, MouthY + 0.1f, 0.13f, 0.018f, MouthY + 0.03f, true)));
                    case 1: // sorrisão com dentes e língua
                    {
                        float m = Intersect(Ellipse(x, y, 0f, MouthY + 0.03f, 0.15f, 0.12f), y - (MouthY + 0.03f));
                        if (m > aa) return c;
                        c = Color.Lerp(c, inside, Cov(m));
                        float teeth = Intersect(m, (MouthY + 0.0f) - y);
                        c = Color.Lerp(c, Color.white, Cov(teeth));
                        float tongue = Intersect(Circle(x, y, 0f, MouthY - 0.1f, 0.07f), m);
                        c = Color.Lerp(c, new Color(0.9f, 0.42f, 0.45f, 1f), Cov(tongue));
                        return c;
                    }
                    case 2: // surpreso "o"
                    {
                        float o = Ellipse(x, y, 0f, MouthY - 0.01f, 0.055f, 0.07f);
                        c = Color.Lerp(c, lip, Cov(o - 0.0f));
                        return Color.Lerp(c, inside, Cov(o + 0.016f));
                    }
                    case 3: // de lado: meio sorriso só do lado direito
                    {
                        float flat = Capsule(x, y, -0.09f, MouthY - 0.005f, 0.0f, MouthY - 0.015f, 0.017f);
                        float curve = Intersect(ArcStroke(x, y, 0.0f, MouthY + 0.1f, 0.12f, 0.018f, MouthY + 0.03f, true), -x);
                        return Color.Lerp(c, lip, Cov(Union(flat, curve)));
                    }
                    default: // sério
                        return Color.Lerp(c, lip, Cov(Capsule(x, y, -0.08f, MouthY, 0.08f, MouthY, 0.017f)));
                }
            }

            private Color DrawBeard(Color c, float x, float y, float head)
            {
                Color b = HairShade(x, y, Mul(hairColor, 0.92f));
                switch (beard)
                {
                    case 1: // cavanhaque
                    {
                        float g = Intersect(Ellipse(x, y, 0f, MouthY - 0.16f, 0.13f, 0.13f), head - 0.01f);
                        g = Union(g, Capsule(x, y, -0.1f, MouthY + 0.07f, 0.1f, MouthY + 0.07f, 0.03f));
                        g = Subtract(g, Ellipse(x, y, 0f, MouthY - 0.01f, 0.13f, 0.06f));
                        return Color.Lerp(c, b, Cov(g));
                    }
                    case 3: // barba cheia
                    {
                        float nx = x / HeadRx;
                        float line = -0.13f + 0.22f * nx * nx;
                        float d = Intersect(head + 0.005f, y - line);
                        d = Union(d, Intersect(Ellipse(x, y, 0f, -0.5f, 0.3f, 0.14f), head - 0.05f));
                        d = Subtract(d, Ellipse(x, y, 0f, MouthY - 0.01f, 0.12f, 0.055f));
                        c = Color.Lerp(c, b, Cov(d));
                        return DrawMustache(c, x, y);
                    }
                    default:
                        return c;
                }
            }

            private Color DrawMustache(Color c, float x, float y)
            {
                float m = Union(Ellipse(x, y, -0.065f, MouthY + 0.075f, 0.085f, 0.032f), Ellipse(x, y, 0.065f, MouthY + 0.075f, 0.085f, 0.032f));
                return Color.Lerp(c, HairShade(x, y, Mul(hairColor, 0.85f)), Cov(m));
            }
        }
    }
}
