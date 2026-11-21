using System.Collections.Generic;
using Fodinha.Hosting;
using UnityEngine;

namespace Fodinha.Game.UI
{
    /// <summary>
    /// Desenha o avatar (boneco com volume: cabeça grande, olhos expressivos, luz e sombra suaves) num círculo,
    /// tudo por código a partir de um <see cref="AvatarSpec"/>. As imagens ficam guardadas para não desenhar de novo.
    /// Coordenadas: x e y de -1 a 1 (y para cima), centro do círculo em (0, 0).
    ///
    /// Volume: cada forma ganha uma "normal" 3D (cabeça = elipsoide; cabelo, roupa e orelhas = borda arredondada)
    /// e é iluminada por uma luz de cima-esquerda, com sombra quente, brilho especular e luz de contorno.
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
        private static readonly Color[] HairColors = { C("2A2522"), C("4A2C1A"), C("7B4A2A"), C("E3BC6A"), C("B9502A"), C("BDBDBD"), C("3E7BD6"), C("E46BA6") };
        private static readonly Color[] EyeColors = { C("7A4A26"), C("3A2A1E"), C("3F9A52"), C("3D80D2") };
        private static readonly Color[] Shirts = { C("2F6FD1"), C("D63B3B"), C("2E9E57"), C("F2C230"), C("7E4AC2"), C("2E2E33"), C("F2F2F2"), C("F07B2A") };
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
            private delegate float Sdf(float x, float y);

            private readonly float aa;
            private readonly int hair, eyes, mouth, beard, earrings;
            private readonly Color skin, skinDark, hairColor, hairLight, iris, shirt, bg, lip;
            private readonly Sdf hairFrontFn, hairBackFn, bodyFn, earsFn;

            // Luz de cima-esquerda, vindo um pouco da frente; "meio-vetor" para o brilho especular.
            private static readonly Vector3 Light = new Vector3(-0.42f, 0.58f, 0.7f).normalized;
            private static readonly Vector3 Half = (Light + Vector3.forward).normalized;

            // Tons das sombras: pele e cabelo puxam para o quente; roupa, para o frio.
            private static readonly Color SkinTint = new Color(0.72f, 0.52f, 0.52f, 1f);
            private static readonly Color HairTint = new Color(0.5f, 0.42f, 0.42f, 1f);
            private static readonly Color ClothTint = new Color(0.58f, 0.58f, 0.68f, 1f);

            // cabeça
            private const float HeadX = 0f, HeadY = 0.06f, HeadRx = 0.47f, HeadRy = 0.56f;
            private const float EyeY = 0.03f, EyeX = 0.19f;
            private const float NoseY = -0.1f;
            private const float MouthY = -0.28f;

            public Ctx(AvatarSpec s, float aa)
            {
                this.aa = aa;
                hair = s[AvatarSpec.Part.Hair];
                eyes = s[AvatarSpec.Part.Eyes];
                mouth = s[AvatarSpec.Part.Mouth];
                beard = s[AvatarSpec.Part.Beard];
                earrings = s[AvatarSpec.Part.Earrings];
                skin = Skins[s[AvatarSpec.Part.Skin]];
                skinDark = Mul(skin, 0.78f);
                hairColor = HairColors[s[AvatarSpec.Part.HairColor]];
                hairLight = Color.Lerp(hairColor, Color.white, 0.35f);
                iris = EyeColors[s[AvatarSpec.Part.EyeColor]];
                shirt = Shirts[s[AvatarSpec.Part.Shirt]];
                bg = Backgrounds[s[AvatarSpec.Part.Background]];
                lip = Color.Lerp(Mul(skin, 0.8f), new Color(0.84f, 0.36f, 0.4f, 1f), 0.5f);

                hairFrontFn = HairFront;
                hairBackFn = HairBack;
                bodyFn = Body;
                earsFn = Ears;
            }

            private static Color Mul(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);
            private static Color C2(float r, float g, float b) => new Color(r, g, b, 1f);

            /// <summary>Cobertura (0..1) a partir da distância com sinal (negativa = dentro), com borda suave.</summary>
            private float Cov(float d) => Mathf.Clamp01(0.5f - d / aa);

            // ------------------------------------------------------------------ formas (distância com sinal)

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

            /// <summary>Segmento com pontas redondas e espessura que muda de uma ponta à outra.</summary>
            private static float Capsule(float x, float y, float ax, float ay, float bx, float by, float ra, float rb)
            {
                float pax = x - ax, pay = y - ay, bax = bx - ax, bay = by - ay;
                float h = Mathf.Clamp01((pax * bax + pay * bay) / (bax * bax + bay * bay));
                float dx = pax - bax * h, dy = pay - bay * h;
                return Mathf.Sqrt(dx * dx + dy * dy) - Mathf.Lerp(ra, rb, h);
            }

            private static float Capsule(float x, float y, float ax, float ay, float bx, float by, float r)
                => Capsule(x, y, ax, ay, bx, by, r, r);

            private static float Box(float x, float y, float cx, float cy, float hw, float hh, float r)
            {
                float qx = Mathf.Abs(x - cx) - hw + r, qy = Mathf.Abs(y - cy) - hh + r;
                float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
                return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
            }

            private static float Union(float a, float b) => Mathf.Min(a, b);

            /// <summary>União "derretida": as formas se juntam sem emenda (k = largura da junção).</summary>
            private static float Blend(float a, float b, float k)
            {
                float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
                return Mathf.Lerp(b, a, h) - k * h * (1f - h);
            }
            private static float Intersect(float a, float b) => Mathf.Max(a, b);
            private static float Subtract(float a, float b) => Mathf.Max(a, -b);

            /// <summary>Arco (traço) de uma circunferência, só a parte abaixo/acima de um limite em y.</summary>
            private static float ArcStroke(float x, float y, float cx, float cy, float r, float half, float yLimit, bool below)
            {
                float ring = Mathf.Abs(Circle(x, y, cx, cy, r)) - half;
                float cut = below ? y - yLimit : yLimit - y;
                return Mathf.Max(ring, cut);
            }

            /// <summary>Mancha suave (1 no centro, 0 no raio).</summary>
            private static float Glow(float x, float y, float cx, float cy, float radius)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / radius;
                return d >= 1f ? 0f : (1f - d * d) * (1f - d * d);
            }

            // Cabeça um pouco mais estreita no queixo (formato de ovo).
            private static float EggK(float y)
            {
                float t = Mathf.Clamp01((HeadY - y) / HeadRy);
                return 1f + 0.16f * t * t;
            }

            private static float Head(float x, float y) => Ellipse(x * EggK(y), y, HeadX, HeadY, HeadRx, HeadRy);

            private static float Body(float x, float y) => Ellipse(x, y, 0f, -1.18f, 0.80f, 0.52f);

            private static float Ears(float x, float y)
                => Union(Ellipse(x, y, -0.475f, 0.0f, 0.085f, 0.115f), Ellipse(x, y, 0.475f, 0.0f, 0.085f, 0.115f));

            // ------------------------------------------------------------------ luz

            /// <summary>Ilumina uma cor com a normal dada: sombra colorida, luz "envolvente", brilho e contorno.</summary>
            private Color Lit(Color albedo, Vector3 n, Color shadowTint, float specAmount, float shininess)
            {
                float ndl = Vector3.Dot(n, Light);
                float wrap = Mathf.Clamp01((ndl + 0.45f) / 1.45f);
                var shadow = new Color(albedo.r * shadowTint.r, albedo.g * shadowTint.g, albedo.b * shadowTint.b, 1f);
                Color col = Color.Lerp(shadow, Mul(albedo, 1.07f), wrap);
                float spec = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(n, Half)), shininess) * specAmount;
                float rim = Mathf.Pow(1f - Mathf.Clamp01(n.z), 3f) * 0.22f; // luz do fundo batendo na borda
                col.r += spec + bg.r * rim;
                col.g += spec + bg.g * rim;
                col.b += spec + bg.b * rim;
                col.a = 1f;
                return col;
            }

            /// <summary>Normal de uma forma com borda arredondada (perfil de quarto de círculo de largura w).</summary>
            private static Vector3 Bevel(Sdf f, float x, float y, float d, float w)
            {
                const float e = 0.004f;
                float gx = f(x + e, y) - d, gy = f(x, y + e) - d;
                float len = Mathf.Sqrt(gx * gx + gy * gy);
                if (len < 1e-6f) return Vector3.forward;
                gx /= len;
                gy /= len;
                float s = 1f - Mathf.Clamp01(-d / w);
                float h = Mathf.Sqrt(Mathf.Max(0.0001f, 1f - s * s));
                return new Vector3(s * gx, s * gy, h).normalized;
            }

            /// <summary>Normal do rosto (elipsoide achatado na frente, como boneco).</summary>
            private static Vector3 HeadNormal(float x, float y)
            {
                float lx = (x - HeadX) * EggK(y) / HeadRx, ly = (y - HeadY) / HeadRy;
                float nz = Mathf.Sqrt(Mathf.Max(0f, 1f - lx * lx - ly * ly));
                return new Vector3(lx * 0.85f, ly * 0.8f, nz + 0.3f).normalized;
            }

            // ------------------------------------------------------------------ pixel

            public Color32 Shade(float x, float y)
            {
                float disc = Circle(x, y, 0f, 0f, 1f);
                float alpha = Cov(disc);
                if (alpha <= 0f) return new Color32(0, 0, 0, 0);

                // Fundo: degradê suave + sombra do boneco projetada atrás (dá profundidade).
                float r = Mathf.Sqrt(x * x + y * y);
                Color c = Color.Lerp(Color.Lerp(bg, Color.white, 0.32f), Mul(bg, 0.84f), r * r);
                float cast = Union(Head(x - 0.06f, y + 0.05f), Body(x - 0.06f, y + 0.05f));
                c = Mul(c, 1f - 0.2f * Mathf.Clamp01(1f - cast / 0.14f));

                // Cabelo de trás.
                float back = HairBack(x, y);
                if (back < aa) c = Color.Lerp(c, HairLit(x, y, Bevel(hairBackFn, x, y, back, 0.16f), 0.7f), Cov(back));

                // Roupa: primeiro o "fundo" da gola (mais escuro), depois o pescoço, depois a frente da camiseta.
                float body = Body(x, y);
                float hole = Ellipse(x, y, 0f, -0.72f, 0.17f, 0.09f);
                if (body < aa) c = Color.Lerp(c, Mul(shirt, 0.45f), Cov(body));

                float neck = Box(x, y, 0f, -0.6f, 0.15f, 0.22f, 0.08f);
                if (neck < aa)
                {
                    float nx = Mathf.Clamp(x / 0.16f, -1f, 1f);
                    Color n = Lit(skin, new Vector3(nx * 0.8f, -0.1f, Mathf.Sqrt(1f - nx * nx) + 0.2f).normalized, SkinTint, 0.04f, 20f);
                    // Sombra do queixo no pescoço.
                    float chin = Head(x, y + 0.07f);
                    n = Mul(n, 1f - 0.32f * Mathf.Clamp01(1f - chin / 0.1f));
                    c = Color.Lerp(c, n, Cov(neck));
                }

                float front = Subtract(body, hole);
                if (front < aa)
                {
                    Color s = Lit(shirt, Bevel(bodyFn, x, y, body, 0.32f), ClothTint, 0.06f, 10f);
                    // Sombra do queixo na camiseta.
                    s = Mul(s, 1f - 0.18f * Mathf.Clamp01(1f - Head(x, y + 0.2f) / 0.15f));
                    c = Color.Lerp(c, s, Cov(front));
                    float rib = Intersect(Mathf.Abs(hole) - 0.022f, body);
                    c = Color.Lerp(c, Mul(Lit(shirt, Bevel(bodyFn, x, y, body, 0.32f), ClothTint, 0f, 1f), 0.85f), Cov(rib));
                }

                // Orelhas.
                float ears = Ears(x, y);
                if (ears < aa)
                {
                    c = Color.Lerp(c, Lit(skin, Bevel(earsFn, x, y, ears, 0.07f), SkinTint, 0.08f, 20f), Cov(ears));
                    float inner = Union(Ellipse(x, y, -0.485f, 0.0f, 0.04f, 0.065f), Ellipse(x, y, 0.485f, 0.0f, 0.04f, 0.065f));
                    c = Color.Lerp(c, Mul(skinDark, 0.82f), Mathf.Clamp01(1f - inner / 0.03f) * 0.55f);
                }

                // Rosto.
                float head = Head(x, y);
                Vector3 faceN = HeadNormal(x, y);
                if (head < aa)
                {
                    Color face = Lit(skin, faceN, SkinTint, 0.13f, 26f);
                    // Cavidade dos olhos e luz no queixo/nariz deixam o rosto menos "chapado".
                    float sockets = Mathf.Min(Glow(x, y, -EyeX, EyeY + 0.02f, 0.17f) + Glow(x, y, EyeX, EyeY + 0.02f, 0.17f), 1f);
                    face = Color.Lerp(face, Mul(face, 0.92f), sockets * 0.6f);
                    float cheek = Mathf.Min(Glow(x, y, -0.27f, -0.14f, 0.12f), 1f) + Mathf.Min(Glow(x, y, 0.27f, -0.14f, 0.12f), 1f);
                    face = Color.Lerp(face, new Color(0.96f, 0.45f, 0.45f, 1f), cheek * 0.2f);
                    c = Color.Lerp(c, face, Cov(head));
                }

                if (beard == 1 || beard == 3) c = DrawBeard(c, x, y, head, faceN);
                c = DrawNose(c, x, y);
                c = DrawEyes(c, x, y);
                c = DrawBrows(c, x, y);
                c = DrawMouth(c, x, y);
                if (beard == 2 || beard == 3) c = DrawMustache(c, x, y, faceN);
                if (earrings > 0) c = DrawEarrings(c, x, y);

                // Cabelo da frente: sombra na testa logo abaixo dele, depois o cabelo com volume e brilho.
                float hairFront = HairFront(x, y);
                if (hairFront > 0f && hairFront < 0.07f && head < 0f)
                    c = Color.Lerp(c, Mul(c, 0.78f), (1f - hairFront / 0.07f) * 0.65f);
                if (hairFront < aa)
                    c = Color.Lerp(c, HairLit(x, y, Bevel(hairFrontFn, x, y, hairFront, HairBevelWidth), 1f), Cov(hairFront));
                if (hair == 6) // moicano: laterais raspadas (só sobre a cabeça)
                {
                    float shaved = Subtract(Intersect(head, 0.2f - y), Box(x, y, 0f, 0.4f, 0.12f, 0.5f, 0.05f));
                    c = Color.Lerp(c, hairColor, Cov(shaved) * 0.14f);
                }

                // Contorno sutil do círculo.
                c = Color.Lerp(c, Mul(bg, 0.7f), Mathf.Clamp01(1f + disc / 0.03f) * 0.5f);

                c.a = alpha;
                return c;
            }

            // ------------------------------------------------------------------ cabelo

            private float HairBevelWidth => hair == 3 ? 0.07f : hair == 4 || hair == 7 || hair == 8 ? 0.09f : 0.15f;

            /// <summary>Cabelo com volume: fios saindo do redemoinho, sombra quente e faixa de brilho.</summary>
            private Color HairLit(float x, float y, Vector3 n, float dark)
            {
                float ang = Mathf.Atan2(y - 0.72f, x + 0.08f);
                float wave = Mathf.Sin(ang * 44f + Mathf.Sin(ang * 6f + y * 5f) * 2.2f);
                float clump = Mathf.Sin(ang * 11f + 0.7f);
                Color albedo = Mul(hairColor, dark * (0.96f + 0.02f * wave + 0.025f * clump));
                var nn = new Vector3(n.x + (wave * 0.05f + clump * 0.06f) * n.z, n.y, n.z).normalized;
                Color col = Lit(albedo, nn, HairTint, 0f, 1f);
                float spec = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(nn, Half)), 12f) * (0.6f + 0.4f * wave);
                Color sheen = Color.Lerp(hairLight, Color.white, 0.3f);
                return Color.Lerp(col, sheen, Mathf.Clamp01(spec) * 0.42f * dark);
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
                        return Blend(Ellipse(x, y, 0f, 0.12f, 0.6f, 0.62f), Box(x, y, 0f, -0.3f, 0.6f, 0.42f, 0.18f), 0.1f);
                    case 7: // franja: um pouco de volume atrás
                        return Ellipse(x, y, 0f, 0.1f, 0.56f, 0.6f);
                    case 8: // chanel: corte reto na altura do queixo
                        return Intersect(Union(Ellipse(x, y, 0f, 0.12f, 0.6f, 0.6f), Box(x, y, 0f, -0.1f, 0.6f, 0.3f, 0.2f)), -0.4f - y);
                    case 9: // rabo de cavalo: aparece caindo atrás, do lado direito
                        return Blend(Capsule(x, y, 0.3f, 0.5f, 0.6f, 0.02f, 0.07f, 0.11f), Capsule(x, y, 0.6f, 0.02f, 0.62f, -0.32f, 0.11f, 0.03f), 0.06f);
                    default:
                        return 10f;
                }
            }

            /// <summary>Costeletas curtas, coladas ao rosto.</summary>
            private static float Burns(float x, float y)
                => Sides(x, y, 0.445f, 0.28f, 0.45f, 0.13f, 0.03f, 0.02f);

            /// <summary>Mechas laterais (dos dois lados), afinando ou engrossando de cima para baixo.</summary>
            private static float Sides(float x, float y, float topX, float topY, float bottomX, float bottomY, float topR, float bottomR)
            {
                float ax = Mathf.Abs(x);
                return Capsule(ax, y, topX, topY, bottomX, bottomY, topR, bottomR);
            }

            private float HairFront(float x, float y)
            {
                // "Touca" que cobre o alto da cabeça, recortada por uma linha do cabelo.
                float cap = Ellipse(x, y, 0f, HeadY + 0.035f, HeadRx + 0.04f, HeadRy + 0.05f);
                float nx = x / 0.5f;
                switch (hair)
                {
                    case 1: // curto
                    {
                        float line = 0.27f + 0.07f * nx * nx + 0.018f * Mathf.Sin(x * 26f);
                        return Blend(Intersect(cap, line - y), Intersect(Burns(x, y), cap), 0.05f);
                    }
                    case 2: // topete
                    {
                        float line = 0.28f + 0.07f * nx * nx + 0.02f * Mathf.Sin(x * 20f + 1f);
                        float d = Intersect(cap, line - y);
                        d = Blend(d, Ellipse(x, y, 0.06f, 0.6f, 0.36f, 0.16f), 0.06f);
                        d = Blend(d, Ellipse(x, y, 0.2f, 0.66f, 0.2f, 0.11f), 0.05f);
                        return Blend(d, Intersect(Burns(x, y), cap), 0.05f);
                    }
                    case 3: // cacheado: bolinhas na testa e no alto
                    {
                        float d = Intersect(cap, 0.3f - 0.1f * nx * nx - y);
                        for (int i = 0; i < 9; i++)
                        {
                            float a = Mathf.Deg2Rad * (15f + i * 18.75f);
                            d = Blend(d, Circle(x, y, Mathf.Cos(a) * 0.45f, 0.16f + Mathf.Sin(a) * 0.45f, 0.12f), 0.02f);
                        }
                        return d;
                    }
                    case 4: // longo, repartido de lado
                    {
                        float line = 0.36f - 0.2f * (nx + 0.35f) * (nx + 0.35f);
                        float d = Intersect(cap, line - y);
                        return Blend(d, Sides(x, y, 0.46f, 0.25f, 0.52f, -0.66f, 0.085f, 0.11f), 0.08f);
                    }
                    case 5: // coque
                    {
                        float line = 0.3f + 0.05f * nx * nx;
                        return Blend(Intersect(cap, line - y), Circle(x, y, 0f, 0.74f, 0.17f), 0.06f);
                    }
                    case 6: // moicano
                        return Intersect(Box(x, y, 0f, 0.5f, 0.1f, 0.3f, 0.08f), Ellipse(x, y, 0f, 0.12f, 0.52f, 0.72f));
                    case 7: // franja reta com pontas
                    {
                        float line = 0.17f + 0.035f * Mathf.Abs(Mathf.Sin(x * 22f));
                        float d = Intersect(cap, line - y);
                        return Blend(d, Sides(x, y, 0.46f, 0.2f, 0.48f, -0.17f, 0.07f, 0.06f), 0.06f);
                    }
                    case 8: // chanel: franja cheia e laterais até o queixo
                    {
                        float line = 0.2f + 0.03f * nx * nx + 0.012f * Mathf.Abs(Mathf.Sin(x * 16f));
                        float d = Intersect(cap, line - y);
                        return Blend(d, Sides(x, y, 0.47f, 0.2f, 0.51f, -0.34f, 0.09f, 0.1f), 0.07f);
                    }
                    case 9: // rabo de cavalo: puxado para trás, testa livre
                    {
                        float line = 0.33f + 0.05f * nx * nx;
                        return Blend(Intersect(cap, line - y), Intersect(Burns(x, y), cap), 0.05f);
                    }
                    default: // careca
                        return 10f;
                }
            }

            // ------------------------------------------------------------------ rosto

            private Color DrawNose(Color c, float x, float y)
            {
                // Sombra embaixo do nariz (a luz vem de cima).
                c = Color.Lerp(c, Mul(c, 0.84f), Mathf.Min(Glow(x, y, 0.015f, NoseY - 0.05f, 0.075f), 1f) * 0.55f);
                float nose = Ellipse(x, y, 0f, NoseY, 0.062f, 0.055f);
                if (nose < aa)
                {
                    float lx = x / 0.062f, ly = (y - NoseY) / 0.055f;
                    var n = new Vector3(lx * 0.55f, ly * 0.55f, Mathf.Sqrt(Mathf.Max(0f, 1f - lx * lx - ly * ly)) + 0.45f).normalized;
                    c = Color.Lerp(c, Lit(skin, n, SkinTint, 0.22f, 30f), Cov(nose) * 0.85f);
                }
                float nostrils = Union(Ellipse(x, y, -0.03f, NoseY - 0.032f, 0.014f, 0.009f), Ellipse(x, y, 0.03f, NoseY - 0.032f, 0.014f, 0.009f));
                return Color.Lerp(c, Mul(skinDark, 0.7f), Cov(nostrils) * 0.55f);
            }

            private Color DrawEyes(Color c, float x, float y)
            {
                Color lashCol = C2(0.1f, 0.07f, 0.06f);
                for (int side = -1; side <= 1; side += 2)
                {
                    float cx = side * EyeX;
                    if (Circle(x, y, cx, EyeY, 0.2f) > 0f) continue;
                    int style = eyes == 4 && side > 0 ? 2 : eyes; // piscando: olho direito fechado
                    float outer = side * (x - cx); // > 0 = canto de fora

                    if (style == 2)
                    {
                        // Fechado sorrindo: arco "∩" mais grosso no meio e um cílio no canto.
                        float t = Mathf.Clamp01(1f - Mathf.Abs(x - cx) / 0.09f);
                        float arc = ArcStroke(x, y, cx, EyeY - 0.055f, 0.08f, 0.007f + 0.011f * t, EyeY - 0.05f, false);
                        c = Color.Lerp(c, lashCol, Cov(arc));
                        float flick = Capsule(x, y, cx + side * 0.072f, EyeY - 0.012f, cx + side * 0.1f, EyeY + 0.01f, 0.007f, 0.003f);
                        c = Color.Lerp(c, lashCol, Cov(flick));
                        continue;
                    }

                    float s = style == 1 ? 1.16f : 1f;
                    float rx = 0.105f * s, ry = 0.1f * s;
                    float sclera = Ellipse(x, y, cx, EyeY, rx, ry);

                    // Dobra da pálpebra acima do olho.
                    float crease = Intersect(Mathf.Abs(Ellipse(x, y, cx, EyeY + 0.012f, rx + 0.026f, ry + 0.032f)) - 0.005f, (EyeY + ry * 0.35f) - y);
                    c = Color.Lerp(c, Mul(skinDark, 0.85f), Cov(crease) * 0.4f);

                    if (sclera < aa)
                    {
                        // Branco do olho: mais escuro nas bordas e embaixo da pálpebra.
                        float qx = (x - cx) / rx, qy = (y - EyeY) / ry;
                        float q = Mathf.Clamp01(qx * qx + qy * qy);
                        Color white = Color.Lerp(C2(0.98f, 0.98f, 0.99f), C2(0.8f, 0.82f, 0.88f), q * q);
                        white = Color.Lerp(white, C2(0.72f, 0.7f, 0.75f), Mathf.Clamp01(qy) * 0.35f);
                        c = Color.Lerp(c, white, Cov(sclera));

                        // Íris grande com anel escuro, fibras e luz entrando por baixo.
                        float ix = cx + 0.008f, iy = EyeY - 0.008f, ri = 0.072f * s;
                        float di = Circle(x, y, ix, iy, ri);
                        float irisD = Intersect(di, sclera);
                        if (irisD < aa)
                        {
                            float t = Mathf.Clamp01((di + ri) / ri); // 0 centro, 1 borda
                            float fib = Mathf.Sin(Mathf.Atan2(y - iy, x - ix) * 16f) * 0.06f;
                            Color ic = Color.Lerp(Color.Lerp(iris, Color.white, 0.18f), Mul(iris, 0.7f), t);
                            ic = Mul(ic, 1f + fib + 0.25f * Mathf.Clamp01((iy - y) / ri));
                            ic = Color.Lerp(ic, Mul(iris, 0.3f), Mathf.Clamp01((t - 0.82f) / 0.18f)); // anel
                            ic = Color.Lerp(ic, Mul(ic, 0.6f), Mathf.Clamp01((y - (EyeY + ry * 0.25f)) / (ry * 0.75f)) * 0.6f); // sombra da pálpebra
                            c = Color.Lerp(c, ic, Cov(irisD));
                            c = Color.Lerp(c, C2(0.04f, 0.03f, 0.03f), Cov(Intersect(Circle(x, y, ix, iy, 0.034f * s), sclera)));
                            c = Color.Lerp(c, Color.white, Cov(Circle(x, y, ix + 0.026f * s, iy + 0.03f * s, 0.021f * s)) * 0.95f);
                            c = Color.Lerp(c, Color.white, Cov(Circle(x, y, ix - 0.024f * s, iy - 0.03f * s, 0.009f * s)) * 0.75f);
                        }
                    }

                    // Cílios de cima: linha que engrossa no canto de fora + duas "pontinhas".
                    float tk = 0.01f + 0.012f * Mathf.Clamp01(outer / rx * 0.5f + 0.5f);
                    float lash = Intersect(Intersect(sclera - tk, -(sclera + 0.004f)), (EyeY - ry * 0.05f) - y);
                    c = Color.Lerp(c, lashCol, Cov(lash));
                    float f1 = Capsule(x, y, cx + side * rx * 0.82f, EyeY + ry * 0.5f, cx + side * (rx + 0.04f), EyeY + ry * 0.95f, 0.008f, 0.003f);
                    float f2 = Capsule(x, y, cx + side * rx * 0.98f, EyeY + ry * 0.12f, cx + side * (rx + 0.045f), EyeY + ry * 0.45f, 0.007f, 0.003f);
                    c = Color.Lerp(c, lashCol, Cov(Union(f1, f2)));
                    // Linha de baixo, bem leve.
                    float low = Intersect(Mathf.Abs(sclera) - 0.004f, y - (EyeY - ry * 0.3f));
                    c = Color.Lerp(c, Mul(skinDark, 0.7f), Cov(low) * 0.35f);

                    if (style == 3)
                    {
                        // Sonolento: pálpebra (com volume) cobre a metade de cima.
                        float lidEdge = EyeY + 0.008f;
                        float lid = Intersect(Ellipse(x, y, cx, EyeY, rx + 0.014f, ry + 0.014f), lidEdge - y);
                        Vector3 ln = new Vector3((x - cx) / rx * 0.5f, 0.5f, 0.8f).normalized;
                        c = Color.Lerp(c, Lit(skin, ln, SkinTint, 0.12f, 20f), Cov(lid));
                        float lidLine = Intersect(Mathf.Abs(y - lidEdge) - 0.011f, Ellipse(x, y, cx, EyeY, rx + 0.006f, ry));
                        c = Color.Lerp(c, lashCol, Cov(lidLine));
                    }
                }
                return c;
            }

            private Color DrawBrows(Color c, float x, float y)
            {
                float lift = eyes == 1 ? 0.03f : eyes == 2 ? 0.02f : eyes == 3 ? -0.015f : 0f;
                Color brow = hair == 0 && beard == 0 ? Mul(skinDark, 0.62f) : Mul(hairColor, 0.82f);
                if (hairColor.r > 0.8f && hairColor.g > 0.6f) brow = Mul(hairColor, 0.68f); // loiro: sobrancelha mais visível
                for (int side = -1; side <= 1; side += 2)
                {
                    float ix = side * 0.095f, mx = side * 0.2f, ox = side * 0.295f;
                    float iy = EyeY + 0.16f + lift, my = EyeY + 0.19f + lift, oy = EyeY + 0.15f + lift;
                    float d = Union(Capsule(x, y, ix, iy, mx, my, 0.027f, 0.022f), Capsule(x, y, mx, my, ox, oy, 0.022f, 0.011f));
                    if (d > aa) continue;
                    float t = Mathf.Clamp01((y - iy + 0.03f) / 0.07f);
                    c = Color.Lerp(c, Color.Lerp(Mul(brow, 0.85f), brow, t), Cov(d));
                }
                return c;
            }

            /// <summary>Lábio com volume e um pontinho de brilho.</summary>
            private Color LipShade(float x, float y, float cy, float ry)
            {
                float t = Mathf.Clamp01((y - cy) / ry * 0.5f + 0.5f);
                Color col = Color.Lerp(Mul(lip, 0.78f), Color.Lerp(lip, Color.white, 0.12f), t);
                float gloss = Mathf.Min(Glow(x, y, -0.02f, cy + ry * 0.25f, 0.035f), 1f);
                return Color.Lerp(col, Color.white, gloss * 0.45f);
            }

            private Color DrawMouth(Color c, float x, float y)
            {
                Color line = C2(0.34f, 0.12f, 0.12f);
                Color inside = C2(0.3f, 0.06f, 0.08f);
                switch (mouth)
                {
                    case 0: // sorriso: lábio de baixo cheio + linha curvada
                    {
                        float lower = Ellipse(x, y, 0f, MouthY - 0.035f, 0.075f, 0.03f);
                        c = Color.Lerp(c, LipShade(x, y, MouthY - 0.035f, 0.03f), Cov(lower));
                        float smile = ArcStroke(x, y, 0f, MouthY + 0.11f, 0.13f, 0.01f, MouthY + 0.035f, true);
                        c = Color.Lerp(c, line, Cov(smile));
                        float dimples = Union(Circle(x, y, -0.12f, MouthY + 0.02f, 0.012f), Circle(x, y, 0.12f, MouthY + 0.02f, 0.012f));
                        return Color.Lerp(c, Mul(skinDark, 0.85f), Cov(dimples) * 0.5f);
                    }
                    case 1: // sorrisão com dentes e língua
                    {
                        float m = Intersect(Ellipse(x, y, 0f, MouthY + 0.03f, 0.15f, 0.13f), y - (MouthY + 0.03f));
                        if (m > 0.03f) return c;
                        c = Color.Lerp(c, LipShade(x, y, MouthY - 0.08f, 0.05f), Cov(m - 0.014f));
                        float depth = Mathf.Clamp01((MouthY + 0.03f - y) / 0.13f);
                        c = Color.Lerp(c, Color.Lerp(Mul(inside, 0.7f), inside, depth), Cov(m));
                        // Dentes de cima: faixa no alto da boca, mais escura nos cantos.
                        float teeth = Intersect(m, (MouthY - 0.02f) - y);
                        Color tooth = Color.Lerp(C2(0.97f, 0.96f, 0.94f), C2(0.8f, 0.78f, 0.78f), Mathf.Clamp01(Mathf.Abs(x) / 0.14f));
                        c = Color.Lerp(c, tooth, Cov(teeth));
                        float tongue = Intersect(Ellipse(x, y, 0f, MouthY - 0.105f, 0.075f, 0.05f), m);
                        Color tc = Color.Lerp(C2(0.78f, 0.3f, 0.35f), C2(0.95f, 0.5f, 0.52f), Mathf.Clamp01((y - (MouthY - 0.14f)) / 0.06f));
                        return Color.Lerp(c, tc, Cov(tongue));
                    }
                    case 2: // surpreso "o" com lábios cheios
                    {
                        float o = Ellipse(x, y, 0f, MouthY - 0.01f, 0.06f, 0.075f);
                        c = Color.Lerp(c, LipShade(x, y, MouthY - 0.01f, 0.075f), Cov(o));
                        return Color.Lerp(c, inside, Cov(o + 0.02f));
                    }
                    case 3: // de lado: meio sorriso só do lado direito
                    {
                        float lower = Ellipse(x, y, 0.02f, MouthY - 0.035f, 0.06f, 0.025f);
                        c = Color.Lerp(c, LipShade(x, y, MouthY - 0.035f, 0.025f), Cov(lower));
                        float flat = Capsule(x, y, -0.08f, MouthY - 0.005f, 0.0f, MouthY - 0.012f, 0.006f, 0.01f);
                        float curve = Intersect(ArcStroke(x, y, 0.0f, MouthY + 0.1f, 0.12f, 0.01f, MouthY + 0.03f, true), -x);
                        return Color.Lerp(c, line, Cov(Union(flat, curve)));
                    }
                    default: // sério: lábios fechados
                    {
                        float upper = Ellipse(x, y, 0f, MouthY + 0.014f, 0.07f, 0.016f);
                        c = Color.Lerp(c, Mul(lip, 0.8f), Cov(upper));
                        float lower = Ellipse(x, y, 0f, MouthY - 0.026f, 0.068f, 0.026f);
                        c = Color.Lerp(c, LipShade(x, y, MouthY - 0.026f, 0.026f), Cov(lower));
                        return Color.Lerp(c, line, Cov(Capsule(x, y, -0.075f, MouthY, 0.075f, MouthY, 0.006f)));
                    }
                }
            }

            /// <summary>Pelos curtos (barba): cor do cabelo com textura "pontilhada", acompanhando o volume do rosto.</summary>
            private Color StubbleLit(float x, float y, Vector3 faceN)
            {
                float noise = Mathf.Repeat(Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.5453f, 1f);
                return Lit(Mul(hairColor, 0.9f + 0.14f * noise), faceN, HairTint, 0.05f, 14f);
            }

            private Color DrawBeard(Color c, float x, float y, float head, Vector3 faceN)
            {
                switch (beard)
                {
                    case 1: // cavanhaque
                    {
                        float g = Intersect(Ellipse(x, y, 0f, MouthY - 0.16f, 0.13f, 0.13f), head - 0.01f);
                        g = Union(g, Capsule(x, y, -0.1f, MouthY + 0.07f, 0.1f, MouthY + 0.07f, 0.03f));
                        g = Subtract(g, Ellipse(x, y, 0f, MouthY - 0.02f, 0.13f, 0.065f));
                        return Color.Lerp(c, StubbleLit(x, y, faceN), Cov(g));
                    }
                    case 3: // barba cheia
                    {
                        float nx = x / HeadRx;
                        float line = -0.13f + 0.22f * nx * nx;
                        float d = Intersect(head + 0.005f, y - line);
                        d = Union(d, Intersect(Ellipse(x, y, 0f, -0.5f, 0.3f, 0.14f), head - 0.05f));
                        d = Subtract(d, Ellipse(x, y, 0f, MouthY - 0.02f, 0.12f, 0.065f));
                        return Color.Lerp(c, StubbleLit(x, y, faceN), Cov(d));
                    }
                    default:
                        return c;
                }
            }

            private Color DrawMustache(Color c, float x, float y, Vector3 faceN)
            {
                float m = Union(Ellipse(x, y, -0.065f, MouthY + 0.075f, 0.085f, 0.032f), Ellipse(x, y, 0.065f, MouthY + 0.075f, 0.085f, 0.032f));
                if (m > aa) return c;
                var n = new Vector3(faceN.x, faceN.y + Mathf.Clamp((y - (MouthY + 0.075f)) / 0.032f, -1f, 1f) * 0.5f, faceN.z).normalized;
                return Color.Lerp(c, StubbleLit(x, y, n), Cov(m));
            }

            /// <summary>Brincos no lóbulo das duas orelhas (bolinha de ouro, argola ou pérola), com brilho de metal.</summary>
            private Color DrawEarrings(Color c, float x, float y)
            {
                Color gold = C2(0.96f, 0.76f, 0.3f);
                for (int side = -1; side <= 1; side += 2)
                {
                    float ex = side * 0.485f;
                    switch (earrings)
                    {
                        case 1:
                            c = Sphere(c, x, y, ex, -0.1f, 0.03f, gold, 0.9f);
                            break;
                        case 2:
                        {
                            float ring = Mathf.Abs(Circle(x, y, ex, -0.16f, 0.055f)) - 0.011f;
                            if (ring > aa) break;
                            float a = Mathf.Atan2(y + 0.16f, x - ex);
                            Vector3 n = new Vector3(Mathf.Cos(a) * 0.6f, Mathf.Sin(a) * 0.6f, 0.8f).normalized;
                            c = Color.Lerp(c, Lit(gold, n, C2(0.55f, 0.4f, 0.25f), 0.9f, 18f), Cov(ring));
                            break;
                        }
                        case 3:
                            c = Sphere(c, x, y, ex, -0.115f, 0.034f, C2(0.95f, 0.94f, 0.9f), 0.7f);
                            break;
                    }
                }
                return c;
            }

            private Color Sphere(Color c, float x, float y, float cx, float cy, float r, Color albedo, float spec)
            {
                float d = Circle(x, y, cx, cy, r);
                if (d > aa) return c;
                float lx = (x - cx) / r, ly = (y - cy) / r;
                var n = new Vector3(lx, ly, Mathf.Sqrt(Mathf.Max(0f, 1f - lx * lx - ly * ly))).normalized;
                return Color.Lerp(c, Lit(albedo, n, C2(0.55f, 0.45f, 0.35f), spec, 24f), Cov(d));
            }
        }
    }
}
