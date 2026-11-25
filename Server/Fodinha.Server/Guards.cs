using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Fodinha.Server
{
    /// <summary>Conta quantas vezes algo aconteceu numa janela de tempo (ex.: contas criadas na última hora).</summary>
    public sealed class SlidingCounter
    {
        private readonly Queue<double> times = new Queue<double>();

        public int Count(double now, double window)
        {
            while (times.Count > 0 && now - times.Peek() >= window) times.Dequeue();
            return times.Count;
        }

        public void Add(double now) => times.Enqueue(now);

        public bool IsEmpty => times.Count == 0;
    }

    /// <summary>
    /// Balde de fichas: cada mensagem gasta uma ficha; as fichas voltam aos poucos. Quem manda rápido demais
    /// fica sem ficha (a mensagem é ignorada); quem insiste é desconectado.
    /// </summary>
    public sealed class MessageBucket
    {
        private readonly int burst;
        private readonly float perSecond;
        private double tokens;
        private double last;
        public int Dropped;
        public bool Kicked;

        public MessageBucket(int burst, float perSecond, double now)
        {
            this.burst = burst;
            this.perSecond = perSecond;
            tokens = burst;
            last = now;
        }

        public bool Take(double now)
        {
            tokens = Math.Min(burst, tokens + (now - last) * perSecond);
            last = now;
            if (tokens >= 1.0)
            {
                tokens -= 1.0;
                if (tokens >= burst / 2.0) Dropped = 0; // voltou a se comportar
                return true;
            }
            Dropped++;
            return false;
        }
    }

    /// <summary>Limites por endereço IP (contas novas, vídeos premiados, chutes de código VIP).</summary>
    public sealed class AddressGuard
    {
        public readonly SlidingCounter NewAccounts = new SlidingCounter();
        public readonly SlidingCounter Rewards = new SlidingCounter();
        public readonly SlidingCounter CodeFailures = new SlidingCounter();
        public double LastSeen;
    }

    /// <summary>Descobre o IP real de quem conecta (atrás do proxy do Render/Cloudflare, quando configurado).</summary>
    public static class ClientAddress
    {
        /// <summary>
        /// <paramref name="trustProxy"/>: só no Render (variável RENDER), onde a Cloudflare preenche o IP real.
        /// Fora dele, os cabeçalhos podem ser falsificados e são ignorados.
        /// </summary>
        public static string Resolve(Func<string, string> header, IPAddress remote, bool trustProxy)
        {
            if (trustProxy)
            {
                foreach (var name in new[] { "CF-Connecting-IP", "True-Client-IP" })
                    if (TryParse(header(name), out var ip)) return Normalize(ip);
                // Plano B: o primeiro da lista é o cliente (segundo o Render).
                var xff = header("X-Forwarded-For");
                if (!string.IsNullOrEmpty(xff) && TryParse(xff.Split(',')[0], out var first)) return Normalize(first);
            }
            return remote != null ? Normalize(remote) : "desconhecido";
        }

        private static bool TryParse(string text, out IPAddress ip)
        {
            ip = null;
            return !string.IsNullOrWhiteSpace(text) && IPAddress.TryParse(text.Trim(), out ip);
        }

        /// <summary>IPv4 como está; IPv6 pelo bloco /64 (o mesmo aparelho troca o final do endereço à vontade).</summary>
        public static string Normalize(IPAddress ip)
        {
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            if (ip.AddressFamily != AddressFamily.InterNetworkV6) return ip.ToString();
            var bytes = ip.GetAddressBytes();
            for (int i = 8; i < 16; i++) bytes[i] = 0;
            return new IPAddress(bytes) + "/64";
        }
    }

    /// <summary>Limpeza do nome do jogador: sem invisíveis, sem truques de texto, sem ofensas graves.</summary>
    public static class NameFilter
    {
        public const int MaxLength = 14;
        public const string Fallback = "Jogador";

        /// <summary>
        /// Palavras proibidas em nomes (ódio, preconceito e crimes graves), comparadas sem acento, sem números
        /// "disfarçados" (h1tl3r) e sem espaços/símbolos. Palavrão comum não entra: o jogo se chama Fodinha.
        /// </summary>
        /// <summary>Proibidas em qualquer lugar do nome (mesmo grudadas em outras letras).</summary>
        private static readonly string[] BlockedAnywhere =
        {
            "hitler", "nazista", "nazismo", "estupr", "pedofil"
        };

        /// <summary>Proibidas como palavra (o começo de uma palavra do nome), para não pegar nomes de verdade
        /// como "Sílvia Dores" (que sem espaço contém uma dessas).</summary>
        private static readonly string[] BlockedWords =
        {
            "viado", "viadinho", "traveco", "sapatao", "boiola", "crioulo", "retardado", "mongoloide"
        };

        public static string Clean(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return Fallback;
            var sb = new StringBuilder(name.Length);
            int marks = 0;
            bool lastSpace = false;
            foreach (char c in name)
            {
                var cat = char.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.Control || cat == UnicodeCategory.Format ||
                    cat == UnicodeCategory.LineSeparator || cat == UnicodeCategory.ParagraphSeparator ||
                    cat == UnicodeCategory.PrivateUse || cat == UnicodeCategory.OtherNotAssigned ||
                    c == '<' || c == '>')
                    continue;
                if (cat == UnicodeCategory.NonSpacingMark || cat == UnicodeCategory.EnclosingMark)
                {
                    if (++marks > 2) continue; // acentos empilhados ("texto zalgo")
                }
                else marks = 0;
                bool space = char.IsWhiteSpace(c);
                if (space && (lastSpace || sb.Length == 0)) continue;
                sb.Append(space ? ' ' : c);
                lastSpace = space;
            }
            string clean = sb.ToString().Trim();
            if (clean.Length > MaxLength) clean = clean.Substring(0, MaxLength).Trim();
            if (clean.Length == 0 || IsOffensive(clean)) return Fallback;
            return clean;
        }

        public static bool IsOffensive(string name)
        {
            string flat = Flatten(name);
            if (BlockedAnywhere.Any(b => flat.Contains(b))) return true;
            foreach (var word in name.Split(new[] { ' ', '_', '-', '.', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string w = Flatten(word);
                if (BlockedWords.Any(b => w.StartsWith(b))) return true;
            }
            return false;
        }

        /// <summary>"H1tl3r_88" → "hitlerbb"; "Sapatão" → "sapatao".</summary>
        private static string Flatten(string text)
        {
            var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (char ch in decomposed)
            {
                char c = ch switch
                {
                    '0' => 'o', '1' => 'i', '3' => 'e', '4' => 'a', '5' => 's', '7' => 't', '8' => 'b',
                    '@' => 'a', '$' => 's', '!' => 'i', '|' => 'i',
                    _ => ch
                };
                if (c >= 'a' && c <= 'z') sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
