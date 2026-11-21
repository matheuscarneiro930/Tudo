using System;
using System.Collections.Generic;
using System.Text;
using Fodinha.Core;

namespace Fodinha.Hosting
{
    /// <summary>
    /// Código de sala com 6 caracteres, sem caracteres que se confundem (0/O, 1/I/L).
    /// É o que os jogadores digitam para entrar numa sala privada.
    /// </summary>
    public static class RoomCode
    {
        public const int Length = 6;
        public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

        public static string Generate(IRandomSource rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            var sb = new StringBuilder(Length);
            for (int i = 0; i < Length; i++) sb.Append(Alphabet[rng.Next(Alphabet.Length)]);
            return sb.ToString();
        }

        /// <summary>Limpa o que o jogador digitou: maiúsculas, sem espaços, hífens ou sublinhados.</summary>
        public static string Normalize(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            var sb = new StringBuilder(input.Length);
            foreach (char raw in input)
            {
                if (char.IsWhiteSpace(raw) || raw == '-' || raw == '_') continue;
                sb.Append(char.ToUpperInvariant(raw));
            }
            return sb.ToString();
        }

        public static bool IsValid(string code)
        {
            if (code == null || code.Length != Length) return false;
            foreach (char c in code)
                if (Alphabet.IndexOf(c) < 0) return false;
            return true;
        }
    }

    /// <summary>Configuração de uma sala online (definida pelo dono da sala).</summary>
    [Serializable]
    public sealed class RoomOptions
    {
        public bool IsPublic = true;
        public int MaxPlayers = 8;
        public RuleSet Rules = RuleSet.CreateDefault();

        /// <summary>Valor da partida em moedas fictícias (cada jogador humano paga ao começar; o vencedor leva o pote).</summary>
        public int Stake = 0;

        /// <summary>Valores que o dono da sala pode escolher (0 = grátis).</summary>
        public static readonly int[] AllowedStakes = { 0, 50, 100, 200, 500, 1000 };

        public RoomOptions Clone()
        {
            var copy = (RoomOptions)MemberwiseClone();
            copy.Rules = (Rules ?? RuleSet.CreateDefault()).Clone();
            return copy;
        }

        public List<string> GetValidationErrors()
        {
            var errors = Rules != null ? Rules.GetValidationErrors() : new List<string> { "Regras ausentes." };
            if (MaxPlayers < RuleSet.MinPlayers || MaxPlayers > RuleSet.MaxPlayers)
                errors.Add("A sala deve ter entre " + RuleSet.MinPlayers + " e " + RuleSet.MaxPlayers + " jogadores.");
            if (Array.IndexOf(AllowedStakes, Stake) < 0)
                errors.Add("Valor da partida inválido.");
            return errors;
        }
    }
}
