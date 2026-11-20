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
            names.AddRange(Fodinha.Hosting.BotNames.Pick(ClampedBots, Seed));
            return names;
        }
    }
}
