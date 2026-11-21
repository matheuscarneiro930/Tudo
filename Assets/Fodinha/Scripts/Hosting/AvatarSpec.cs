using System;
using System.Text;

namespace Fodinha.Hosting
{
    /// <summary>
    /// Avatar do jogador (estilo "boneco 3D" de celular), descrito por escolhas simples: pele, cabelo, olhos...
    /// Trafega na rede e fica salvo como texto curto, ex.: "1-2-3-0-1-0-0-4-5".
    /// O desenho é feito pelo app (cada parte é só um número); o servidor apenas valida e repassa.
    /// </summary>
    public sealed class AvatarSpec : IEquatable<AvatarSpec>
    {
        public const int FormatVersion = 1;

        /// <summary>Tamanho máximo aceito do texto (proteção do servidor).</summary>
        public const int MaxEncodedLength = 48;

        /// <summary>Partes novas entram SEMPRE no fim (textos antigos continuam valendo: o que faltar vira 0).</summary>
        public enum Part { Skin, Hair, HairColor, Eyes, EyeColor, Mouth, Beard, Shirt, Background, Earrings }

        public static readonly int PartCount = Enum.GetValues(typeof(Part)).Length;

        /// <summary>Quantas partes tinha a 1ª versão do avatar (mínimo aceito num texto).</summary>
        public const int OriginalPartCount = 9;

        /// <summary>Nome de cada parte e de cada opção (o número da opção é a posição na lista).</summary>
        public static readonly string[] PartLabels =
            { "Pele", "Cabelo", "Cor do cabelo", "Olhos", "Cor dos olhos", "Boca", "Barba", "Camiseta", "Fundo", "Brincos" };

        public static readonly string[][] OptionLabels =
        {
            new[] { "Muito clara", "Clara", "Média", "Morena", "Escura", "Bem escura" },
            new[] { "Careca", "Curto", "Topete", "Cacheado", "Longo", "Coque", "Moicano", "Franja", "Chanel", "Rabo de cavalo" },
            new[] { "Preto", "Castanho escuro", "Castanho", "Loiro", "Ruivo", "Grisalho", "Azul", "Rosa" },
            new[] { "Normais", "Grandes", "Felizes", "Sonolentos", "Piscando" },
            new[] { "Castanhos", "Pretos", "Verdes", "Azuis" },
            new[] { "Sorriso", "Sorrisão", "Surpreso", "De lado", "Sério" },
            new[] { "Sem barba", "Cavanhaque", "Bigode", "Barba cheia" },
            new[] { "Azul", "Vermelha", "Verde", "Amarela", "Roxa", "Preta", "Branca", "Laranja" },
            new[] { "Azul", "Verde", "Rosa", "Amarelo", "Roxo", "Laranja", "Cinza", "Turquesa" },
            new[] { "Sem brinco", "Bolinha dourada", "Argola", "Pérola" },
        };

        private readonly int[] values = new int[PartCount];

        public int this[Part part]
        {
            get => values[(int)part];
            set => values[(int)part] = Clamp(part, value);
        }

        public static int OptionCount(Part part) => OptionLabels[(int)part].Length;

        private static int Clamp(Part part, int value)
        {
            int max = OptionCount(part) - 1;
            return value < 0 ? 0 : value > max ? max : value;
        }

        public AvatarSpec Clone()
        {
            var copy = new AvatarSpec();
            Array.Copy(values, copy.values, PartCount);
            return copy;
        }

        /// <summary>Texto para salvar/enviar: "versão-parte1-parte2-...".</summary>
        public string Encode()
        {
            var sb = new StringBuilder(24);
            sb.Append(FormatVersion);
            foreach (var v in values) sb.Append('-').Append(v);
            return sb.ToString();
        }

        public override string ToString() => Encode();

        /// <summary>Lê o texto. Falha se o formato estiver errado ou alguma opção não existir.</summary>
        public static bool TryParse(string text, out AvatarSpec spec)
        {
            spec = null;
            if (string.IsNullOrEmpty(text) || text.Length > MaxEncodedLength) return false;
            var parts = text.Split('-');
            int count = parts.Length - 1;
            if (count < OriginalPartCount || count > PartCount || parts[0] != FormatVersion.ToString()) return false;
            var result = new AvatarSpec();
            for (int i = 0; i < count; i++)
            {
                if (!int.TryParse(parts[i + 1], out int v)) return false;
                if (v < 0 || v >= OptionCount((Part)i)) return false;
                result.values[i] = v;
            }
            spec = result;
            return true;
        }

        /// <summary>Texto válido normalizado, ou vazio se for inválido (usado pelo servidor).</summary>
        public static string Sanitize(string text) => TryParse(text, out var spec) ? spec.Encode() : "";

        /// <summary>Lê o texto; se for inválido/vazio, gera um avatar a partir de <paramref name="seed"/> (ex.: nome do bot).</summary>
        public static AvatarSpec ParseOrSeed(string text, string seed) => TryParse(text, out var spec) ? spec : FromSeed(seed);

        /// <summary>Avatar "aleatório" mas sempre igual para a mesma semente (bots e jogadores sem avatar).</summary>
        public static AvatarSpec FromSeed(string seed)
        {
            uint h = Hash(seed ?? "");
            var spec = new AvatarSpec();
            for (int i = 0; i < PartCount; i++)
            {
                h = Next(h);
                spec.values[i] = (int)(h % (uint)OptionCount((Part)i));
            }
            // Cores "fantasia" de cabelo e barba ficam mais raras nos bots.
            if (spec[Part.HairColor] >= 6 && (Next(h) & 3) != 0) spec[Part.HairColor] = (int)(Next(h ^ 0x9E37u) % 5u);
            if (spec[Part.Beard] > 0 && (Next(h ^ 0x51EDu) & 1) == 0) spec[Part.Beard] = 0;
            if (spec[Part.Earrings] > 0 && (Next(h ^ 0x7A3Bu) & 1) == 0) spec[Part.Earrings] = 0;
            return spec;
        }

        /// <summary>Avatar aleatório (botão "Sortear").</summary>
        public static AvatarSpec Random(System.Random rng)
        {
            var spec = new AvatarSpec();
            for (int i = 0; i < PartCount; i++) spec.values[i] = rng.Next(OptionCount((Part)i));
            return spec;
        }

        /// <summary>FNV-1a (estável entre plataformas, ao contrário de string.GetHashCode).</summary>
        private static uint Hash(string s)
        {
            uint h = 2166136261u;
            foreach (char c in s)
            {
                h ^= c;
                h *= 16777619u;
            }
            return h == 0 ? 1u : h;
        }

        private static uint Next(uint x)
        {
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            return x;
        }

        public bool Equals(AvatarSpec other)
        {
            if (other == null) return false;
            for (int i = 0; i < PartCount; i++) if (values[i] != other.values[i]) return false;
            return true;
        }

        public override bool Equals(object obj) => obj is AvatarSpec a && Equals(a);

        public override int GetHashCode() => Encode().GetHashCode();
    }
}
