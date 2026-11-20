using System;
using System.Collections.Generic;
using Fodinha.Core;

namespace Fodinha.Game
{
    /// <summary>Configuração de uma partida offline contra bots.</summary>
    [Serializable]
    public sealed class OfflineMatchConfig
    {
        public const int MinBots = RuleSet.MinPlayers - 1;
        public const int MaxBots = RuleSet.MaxPlayers - 1;

        public string PlayerName = "Você";

        /// <summary>Número de bots (1 a 14). Total de jogadores = bots + 1.</summary>
        public int BotCount = 3;

        public RuleSet Rules = RuleSet.CreateDefault();

        /// <summary>Semente do embaralhamento. 0 = aleatória.</summary>
        public int Seed = 0;

        public int PlayerCount => ClampedBots + 1;

        public int ClampedBots => Math.Max(MinBots, Math.Min(MaxBots, BotCount));

        public OfflineMatchConfig Clone()
        {
            var copy = (OfflineMatchConfig)MemberwiseClone();
            copy.Rules = (Rules ?? RuleSet.CreateDefault()).Clone();
            return copy;
        }

        /// <summary>Nomes dos assentos: assento 0 é o jogador, os demais são bots.</summary>
        public List<string> BuildSeatNames()
        {
            var names = new List<string> { string.IsNullOrWhiteSpace(PlayerName) ? "Você" : PlayerName.Trim() };
            names.AddRange(BotNames.Pick(ClampedBots, Seed));
            return names;
        }
    }

    public static class BotNames
    {
        private static readonly string[] Pool =
        {
            "Zé", "Tonho", "Dona Cida", "Juca", "Bia", "Chico", "Lurdinha", "Nego", "Tião",
            "Rosinha", "Dito", "Neide", "Bené", "Quinzinho", "Tereza", "Biu", "Dadá", "Lelé"
        };

        public static List<string> Pick(int count, int seed)
        {
            var rng = seed == 0 ? new Random() : new Random(seed);
            var pool = new List<string>(Pool);
            var result = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                if (pool.Count == 0) { result.Add("Bot " + (i + 1)); continue; }
                int j = rng.Next(pool.Count);
                result.Add(pool[j]);
                pool.RemoveAt(j);
            }
            return result;
        }
    }
}
