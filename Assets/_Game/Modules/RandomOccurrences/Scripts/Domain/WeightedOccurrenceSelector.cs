using System;
using System.Collections.Generic;

namespace KingdomRuler.Modules.RandomOccurrences.Domain
{
    public static class WeightedOccurrenceSelector
    {
        /// <summary>
        /// Select one event from the pool using weighted random.
        /// Returns null if pool is empty or all weights are zero.
        /// </summary>
        public static RandomOccurrenceDefinition Select(IReadOnlyList<RandomOccurrenceDefinition> pool, Random rng)
        {
            if (pool == null || pool.Count == 0) return null;

            float totalWeight = 0;
            foreach (var e in pool)
            {
                if (e != null) totalWeight += e.Weight;
            }

            if (totalWeight <= 0) return null;

            float roll = (float)(rng.NextDouble() * totalWeight);
            float cumulative = 0;
            foreach (var e in pool)
            {
                if (e == null) continue;
                cumulative += e.Weight;
                if (roll < cumulative) return e;
            }

            return pool[pool.Count - 1]; // fallback
        }
    }
}
