using System;

namespace KingdomRuler.Modules.Trade.Domain
{
    /// <summary>
    /// Prices and sizes a warehouse upgrade (<c>GDD.md</c> §7).
    /// </summary>
    /// <remarks>
    /// Module-owned, not Ledger-owned, per <c>ARCHITECTURE.md</c> §4.3: only Trade upgrades
    /// warehouses, so nothing else has to agree on the result. Trade produces a capacity and the
    /// Ledger derives the regen rate from it.
    /// </remarks>
    public static class WarehouseUpgradeCalculator
    {
        /// <summary>Fraction of the paired warehouse's capacity the resource path costs.</summary>
        public const float PairedCostFraction = 0.8f;

        /// <summary>
        /// The paired-resource upgrade price: 80% of the paired warehouse's <b>capacity</b>,
        /// paid out of the paired resource's <b>stored amount</b>.
        /// </summary>
        /// <remarks>
        /// Capacity, deliberately, not the stored amount — this is the reading GDD §7 flagged for
        /// confirmation and it was confirmed. The consequence is intentional: the player must be
        /// at least 80% full of the paired resource to afford it, so the two warehouses in a pair
        /// advance in step. Pricing it off the stored amount instead would make it cheapest
        /// exactly when the player has least, inverting that pressure.
        /// </remarks>
        public static float PairedPathCost(float pairedResourceCapacity) =>
            Math.Max(0f, pairedResourceCapacity) * PairedCostFraction;

        /// <summary>
        /// Crystal price to go from <paramref name="currentUpgradeLevel"/> to the next level.
        /// Always at least 1 — a free upgrade would be an infinite capacity loop.
        /// </summary>
        public static int CrystalPathCost(int currentUpgradeLevel, WarehouseCurve costCurve)
        {
            float raw = costCurve.ValueAt(Math.Max(0, currentUpgradeLevel));

            // The curve is clamped at MaxSupportedLevel, so this cannot overflow in practice;
            // the guard is here because silently wrapping to a negative price would make an
            // upgrade free (or pay the player) rather than merely mis-priced.
            if (raw >= int.MaxValue) return int.MaxValue;
            return Math.Max(1, (int)Math.Round(raw));
        }

        /// <summary>Capacity of a warehouse at <paramref name="upgradeLevel"/>, where 0 is unupgraded.</summary>
        public static float CapacityAtLevel(int upgradeLevel, WarehouseCurve capacityCurve) =>
            capacityCurve.ValueAt(Math.Max(0, upgradeLevel));
    }
}
