using UnityEngine;
using KingdomRuler.Modules.Trade.Domain;

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
    /// <para><b>Curves are formulas, not per-level tables.</b> This replaced a 6-entry capacity
    /// array and a 5-entry cost array whose fallbacks past the last entry diverged — capacity
    /// grew exponentially while cost grew linearly, so a level-50 warehouse was cheap. See
    /// <see cref="WarehouseCurve"/>.</para>
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
        [Min(1f)]   public float CapacityBase       = WarehouseCurve.DefaultBaseValue;
        [Min(1.01f)] public float CapacityGrowth    = WarehouseCurve.DefaultGrowthFactor;
        [Min(1f)]   public float CapacityRoundTo    = WarehouseCurve.DefaultRoundToNearest;

        [Header("Warehouse crystal price — cost(L) = round(base × growth^L)")]
        [Min(1f)]   public float CrystalCostBase    = 5f;
        [Min(1.01f)] public float CrystalCostGrowth = WarehouseCurve.DefaultGrowthFactor;

        /// <summary>Capacity of a warehouse at a given upgrade level.</summary>
        public WarehouseCurve ToCapacityCurve() =>
            new WarehouseCurve(CapacityBase, CapacityGrowth, CapacityRoundTo);

        /// <summary>Crystal price of the upgrade leaving a given level. Whole crystals, so roundTo is 1.</summary>
        public WarehouseCurve ToCrystalCostCurve() =>
            new WarehouseCurve(CrystalCostBase, CrystalCostGrowth, 1f);

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
