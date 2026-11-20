using System;
using System.Collections.Generic;

namespace Fodinha.Hosting
{
    /// <summary>Nomes dos bots (partida offline e vagas preenchidas por bot nas salas online).</summary>
    public static class BotNames
    {
        private static readonly string[] Pool =
        {
            "Zé", "Tonho", "Dona Cida", "Juca", "Bia", "Chico", "Lurdinha", "Nego", "Tião",
            "Rosinha", "Dito", "Neide", "Bené", "Quinzinho", "Tereza", "Biu", "Dadá", "Lelé"
        };

        /// <summary>Sorteia <paramref name="count"/> nomes diferentes. Semente 0 = aleatório.</summary>
        public static List<string> Pick(int count, int seed, ICollection<string> exclude = null)
        {
            var rng = seed == 0 ? new Random() : new Random(seed);
            var pool = new List<string>();
            foreach (var n in Pool) if (exclude == null || !exclude.Contains(n)) pool.Add(n);

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
