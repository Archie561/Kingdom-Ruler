using System;
using KingdomRuler.Shared.Ledger;

namespace KingdomRuler.Modules.Trade.Domain
{
    /// <summary>
    /// Calculates warehouse upgrade costs for both paths.
    /// </summary>
    public static class WarehouseUpgradeCalculator
    {
        /// <summary>
        /// Resource path cost: 80% of the paired resource's current warehouse capacity.
        /// </summary>
        public static float ResourcePathCost(float pairedResourceCapacity)
        {
            return pairedResourceCapacity * 0.8f;
        }

        /// <summary>
        /// Crystal path cost: look up from a designer-editable curve.
        /// Index = current upgrade level (0-based). Falls back to a default scaling.
        /// </summary>
        public static int CrystalPathCost(int currentUpgradeLevel, int[] crystalCostCurve)
        {
            if (crystalCostCurve != null && currentUpgradeLevel < crystalCostCurve.Length)
                return crystalCostCurve[currentUpgradeLevel];
            // Fallback: 5 + 3 * level
            return 5 + 3 * currentUpgradeLevel;
        }

        /// <summary>
        /// Capacity after upgrade. Look up from curve or apply default scaling.
        /// Index = upgrade level (0 = base). Returns the new capacity.
        /// </summary>
        public static float CapacityAtLevel(int upgradeLevel, float[] capacityCurve)
        {
            if (capacityCurve != null && upgradeLevel < capacityCurve.Length)
                return capacityCurve[upgradeLevel];
            // Fallback: 100 * 1.5^level
            return (float)(100.0 * Math.Pow(1.5, upgradeLevel));
        }
    }
}
