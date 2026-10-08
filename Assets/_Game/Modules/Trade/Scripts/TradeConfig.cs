using System;
using UnityEngine;

namespace KingdomRuler.Modules.Trade
{
    /// <summary>
    /// Tunable parameters for the Trade mechanic (<c>GDD.md</c> §7). One asset; this is what
    /// changes when someone is balancing the game.
    /// </summary>
    /// <remarks>
    /// <para>Config only, no content — offers are generated at runtime from these numbers, not
    /// authored one-asset-each, so <c>ScriptableObjects/Data/</c> stays empty for this module
    /// (<c>ARCHITECTURE.md</c> §7).</para>
    ///
    /// <para><b>Curves are formulas, not per-level tables</b> — the same style as the
    /// characteristic leveling curve (<c>GDD.md</c> §6–§7). This replaced a 6-entry capacity
    /// array and a 5-entry cost array whose fallbacks past the last entry diverged — capacity
    /// grew exponentially while cost grew linearly, so a level-50 warehouse was cheap. A formula
    /// has no edge to fall off. See <see cref="WarehouseCapacityAt"/> and
    /// <see cref="WarehouseUpgradeCrystalCost"/>.</para>
    ///
    /// <para><b>Module-owned, not Ledger-owned.</b> Only Trade upgrades warehouses, so nothing
    /// else has to agree on these numbers (<c>ARCHITECTURE.md</c> §4.3): Trade computes a
    /// capacity and hands the Ledger the number, and the Ledger derives the regen rate from it.</para>
    ///
    /// <para>What does <b>not</b> live here: the 24-hour warehouse refill window, which is
    /// Ledger-owned because the regen rate is derived from capacity (<c>ARCHITECTURE.md</c> §4.3).</para>
    /// </remarks>
    [CreateAssetMenu(fileName = "TradeConfig", menuName = "Kingdom Ruler/Trade/Trade Config")]
    public sealed class TradeConfig : ScriptableObject
    {
        [Header("Offers")]
        [Tooltip("How many offers are shown at once. GDD §7 says 10.")]
        [Min(1)]
        public int OfferCount = 10;

        [Tooltip("Seconds between automatic offer refreshes. GDD §7 says 20 minutes.")]
        [Min(1f)]
        public float OfferRefreshTimeSeconds = 1200f;

        [Tooltip("Crystals to refresh the offer list immediately.")]
        [Min(1)]
        public int InstantRefreshCrystalCost = 3;

        [Tooltip("Units the player gives in a typical offer, before the profitability multiplier.")]
        [Min(1f)]
        public float OfferBaseAmount = 50f;

        [Header("Warehouse capacity — capacity(L) = round(base × growth^L / roundTo) × roundTo")]
        [Min(1f)]   public float CapacityBase       = 100f;
        [Min(1.01f)] public float CapacityGrowth    = 1.5f;
        [Min(1f)]   public float CapacityRoundTo    = 10f;

        [Header("Warehouse crystal price — cost(L) = round(base × growth^L)")]
        [Min(1f)]   public float CrystalCostBase    = 5f;
        [Min(1.01f)] public float CrystalCostGrowth = 1.5f;

        /// <summary>
        /// Highest warehouse level the curves evaluate. A <b>save-integrity guard, not a design
        /// cap</b>: the save is plain JSON on the device, and a hand-edited <c>"Stone": 9999</c>
        /// would produce <c>100 × 1.5^9999</c> → <c>Infinity</c> → an infinite regen rate →
        /// <c>NaN</c> amounts, corrupting the economy irrecoverably (<c>CLAUDE.md</c> §1.2). 50 is
        /// far beyond any reachable play — capacity there is ~6.4 × 10¹⁰ — and inside float range.
        /// </summary>
        public const int MaxWarehouseLevel = 50;

        /// <summary>
        /// Capacity of a warehouse at <paramref name="level"/>, where 0 is unupgraded. Always
        /// finite and positive: a zero capacity would mean a zero regen rate, stranding the
        /// warehouse empty forever.
        /// </summary>
        public float WarehouseCapacityAt(int level) =>
            Grow(CapacityBase, CapacityGrowth, CapacityRoundTo, level);

        /// <summary>
        /// Crystal price of the upgrade leaving <paramref name="currentLevel"/>. Whole crystals,
        /// and always at least 1 — a free upgrade would be an infinite capacity loop.
        /// </summary>
        public int WarehouseUpgradeCrystalCost(int currentLevel)
        {
            float raw = Grow(CrystalCostBase, CrystalCostGrowth, 1f, currentLevel);

            // 5 × 1.5^50 exceeds int.MaxValue even at the level cap; wrapping to a negative price
            // would make the upgrade free, or pay the player, rather than merely mis-priced.
            if (raw >= int.MaxValue) return int.MaxValue;
            return Math.Max(1, (int)Math.Round(raw));
        }

        /// <summary><c>round(base × growth^level / roundTo) × roundTo</c>, held finite and positive.</summary>
        /// <remarks>
        /// <para>The level is clamped to <c>0..</c><see cref="MaxWarehouseLevel"/> rather than
        /// rejected, so a corrupt save degrades to the biggest sane warehouse instead of refusing
        /// to load. Each coefficient is held to a positive minimum and the result floored at
        /// <c>roundTo</c>, so no asset value can produce zero.</para>
        ///
        /// <para><b>The rounding is deliberately <c>Math.Round</c>'s banker's rounding</b>,
        /// exactly as in <c>CharacteristicLevelingCurve</c> — so <c>100 × 1.5² = 225</c> yields
        /// <b>220</b>, not 230. Matching the sibling curve's expression matters more than the
        /// one-unit difference; a test pins it so nobody "corrects" it later.</para>
        /// </remarks>
        private static float Grow(float baseValue, float growth, float roundTo, int level)
        {
            int clamped = Math.Clamp(level, 0, MaxWarehouseLevel);

            baseValue = Math.Max(baseValue, 1f);
            growth    = Math.Max(growth,    0.01f);
            roundTo   = Math.Max(roundTo,   1f);

            double raw     = baseValue * Math.Pow(growth, clamped);
            double rounded = Math.Round(raw / roundTo) * roundTo;
            return (float)Math.Max(rounded, roundTo);
        }

        private void OnValidate()
        {
            // The curves defend themselves too, but catching it here tells the designer
            // immediately instead of silently substituting a different curve than the Inspector
            // shows. Growth must exceed 1 or upgrades get cheaper as they go, which reverses the
            // progression GDD §7 asks for.
            if (OfferCount                < 1)     OfferCount                = 1;
            if (OfferRefreshTimeSeconds   < 1f)    OfferRefreshTimeSeconds   = 1f;
            if (InstantRefreshCrystalCost < 1)     InstantRefreshCrystalCost = 1;
            if (OfferBaseAmount           < 1f)    OfferBaseAmount           = 1f;

            if (CapacityBase    < 1f)    CapacityBase    = 1f;
            if (CapacityGrowth  <= 1f)   CapacityGrowth  = 1.01f;
            if (CapacityRoundTo < 1f)    CapacityRoundTo = 1f;

            if (CrystalCostBase   < 1f)  CrystalCostBase   = 1f;
            if (CrystalCostGrowth <= 1f) CrystalCostGrowth = 1.01f;
        }
    }
}
