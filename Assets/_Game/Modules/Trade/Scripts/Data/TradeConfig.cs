using UnityEngine;

namespace KingdomRuler.Modules.Trade
{
    /// <summary>
    /// Tunable parameters for the Trade mechanic (<c>GDD.md</c> §7). One asset; this is what
    /// changes when someone is balancing the game.
    /// </summary>
    /// <remarks>
    /// <para>Config only, no content — offers are generated at runtime from these numbers, not
    /// authored one-asset-each, so this module has only <c>ScriptableObjects/Config/</c>
    /// (<c>ARCHITECTURE.md</c> §7).</para>
    ///
    /// <para><b>Curves are formulas, not per-level tables</b> — the same style as the
    /// characteristic leveling curve (<c>GDD.md</c> §6–§7), so every level up to the cap is
    /// defined and capacity and price cannot diverge past the end of a table. Only the
    /// coefficients live here; <see cref="WarehouseCurve"/> evaluates them.</para>
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

        [Tooltip("Share of the smaller side's combined warehouse capacity that an offer gives (GDD §7). " +
                 "At most 0.40, which keeps every amount within its own warehouse.")]
        [Range(MinOfferCapacityShare, MaxOfferCapacityShare)]
        public float OfferCapacityShare = 0.20f;

        [Header("Warehouse upgrades")]
        [Tooltip("Highest warehouse level, as the player sees it: levels are shown from 1, so 20 allows " +
                 "19 upgrades (GDD §7). A saved level above it is clamped to it on load.")]
        [Min(1)]
        public int MaxWarehouseLevel = 20;

        [Header("Warehouse capacity — capacity(L) = round(base × growth^L / roundTo) × roundTo")]
        [Min(1f)]   public float CapacityBase       = 100f;
        [Min(1.01f)] public float CapacityGrowth    = 1.5f;
        [Min(1f)]   public float CapacityRoundTo    = 10f;

        [Header("Warehouse crystal price — cost(L) = round(base × growth^L)")]
        [Min(1f)]   public float CrystalCostBase    = 5f;
        [Min(1.01f)] public float CrystalCostGrowth = 1.5f;

        /// <summary>Bounds of <see cref="OfferCapacityShare"/>; above the maximum a single amount could exceed its own warehouse.</summary>
        public const float MinOfferCapacityShare = 0.01f;
        public const float MaxOfferCapacityShare = 0.40f;

        private void OnValidate()
        {
            // The curves defend themselves too, but catching it here tells the designer
            // immediately instead of silently substituting a different curve than the Inspector
            // shows. Growth must exceed 1 or upgrades get cheaper as they go, which reverses the
            // progression GDD §7 asks for.
            if (OfferCount                < 1)     OfferCount                = 1;
            if (OfferRefreshTimeSeconds   < 1f)    OfferRefreshTimeSeconds   = 1f;
            if (InstantRefreshCrystalCost < 1)     InstantRefreshCrystalCost = 1;
            OfferCapacityShare = Mathf.Clamp(OfferCapacityShare, MinOfferCapacityShare, MaxOfferCapacityShare);
            if (MaxWarehouseLevel         < 1)     MaxWarehouseLevel         = 1;

            if (CapacityBase    < 1f)    CapacityBase    = 1f;
            if (CapacityGrowth  <= 1f)   CapacityGrowth  = 1.01f;
            if (CapacityRoundTo < 1f)    CapacityRoundTo = 1f;

            if (CrystalCostBase   < 1f)  CrystalCostBase   = 1f;
            if (CrystalCostGrowth <= 1f) CrystalCostGrowth = 1.01f;
        }
    }
}
