using System;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Trade
{
    /// <summary>
    /// A warehouse's capacity at a level, and the crystal price of the upgrade out of it (GDD §7).
    /// Formulas, not tables, so every level up to the cap is defined; the coefficients are on
    /// <see cref="TradeConfig"/>.
    /// </summary>
    /// <remarks>
    /// <para>Module-owned, not Ledger-owned: only Trade upgrades warehouses, so nothing else has to
    /// agree on these numbers (<c>ARCHITECTURE.md</c> §4.3). Trade hands the Ledger a capacity; the
    /// Ledger derives regen from it.</para>
    ///
    /// <para><b>Two ways of counting levels.</b> A stored level counts upgrades, so 0 is unupgraded;
    /// the screen shows it plus one, and <see cref="TradeConfig.MaxWarehouseLevel"/> is written the
    /// way the player sees it. <see cref="TopLevel"/> converts the one to the other, so nothing else
    /// does that arithmetic.</para>
    ///
    /// <para>Every result is safe whatever the asset or the save holds: the level is clamped to
    /// <c>0..TopLevel</c>, every coefficient is held to a positive minimum, capacity stays
    /// within <c>[roundTo, KingdomLedger.MaxWarehouseCapacity]</c> and a price within
    /// <c>[1, int.MaxValue]</c> — never zero (a free upgrade, or a warehouse that never refills)
    /// and never wrapped negative.</para>
    ///
    /// <para><b>The rounding is <c>Math.Round</c>'s banker's rounding</b>, the same expression as
    /// <c>CharacteristicLevelingCurve</c>: <c>100 × 1.5² = 225</c> gives <b>220</b>, not 230. A test
    /// pins it, so it is not "corrected".</para>
    /// </remarks>
    public static class WarehouseCurve
    {
        /// <summary>The highest stored level: the one the player sees as <see cref="TradeConfig.MaxWarehouseLevel"/>.</summary>
        public static int TopLevel(TradeConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            return Math.Max(0, config.MaxWarehouseLevel - 1);
        }

        /// <summary>Capacity of a warehouse at <paramref name="level"/>; 0 is unupgraded.</summary>
        public static int Capacity(TradeConfig config, int level)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            double roundTo = Math.Max(config.CapacityRoundTo, 1f);
            double raw     = Grow(config.CapacityBase, config.CapacityGrowth, ClampLevel(config, level));
            double rounded = Math.Round(raw / roundTo) * roundTo;
            return (int)Math.Min(Math.Max(rounded, roundTo), KingdomLedger.MaxWarehouseCapacity);
        }

        /// <summary>Crystals for the upgrade out of <paramref name="level"/>, so the first upgrade is <c>CrystalCost(config, 0)</c>.</summary>
        public static int CrystalCost(TradeConfig config, int level)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            double rounded = Math.Round(Grow(config.CrystalCostBase, config.CrystalCostGrowth, ClampLevel(config, level)));
            if (rounded >= int.MaxValue) return int.MaxValue;
            return Math.Max(1, (int)rounded);
        }

        private static int ClampLevel(TradeConfig config, int level) => Math.Clamp(level, 0, TopLevel(config));

        private static double Grow(float baseValue, float growth, int level) =>
            Math.Max(baseValue, 1f) * Math.Pow(Math.Max(growth, 0.01f), level);
    }
}
