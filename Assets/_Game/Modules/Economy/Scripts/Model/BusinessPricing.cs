using System;

namespace KingdomRuler.Modules.Economy
{
    /// <summary>
    /// What units cost, how many the player can afford, and the crystals that cover a shortfall
    /// (GDD §8). Pure and static; <see cref="EconomyManager"/> charges what these return.
    /// </summary>
    /// <remarks>
    /// Overflow-safe throughout: a price too large for <c>long</c> saturates, which simply makes it
    /// unaffordable, and a crystal price stays a <c>long</c> because a large shortfall can exceed
    /// <c>int</c> while the crystal balance cannot.
    /// </remarks>
    public static class BusinessPricing
    {
        /// <summary>The flat price of <paramref name="count"/> units. Saturates at <see cref="long.MaxValue"/>.</summary>
        public static long PriceOf(long unitPrice, int count)
        {
            if (unitPrice <= 0 || count <= 0) return 0;
            return unitPrice > long.MaxValue / count ? long.MaxValue : unitPrice * count;
        }

        /// <summary>How many units <paramref name="gold"/> pays for, at most <paramref name="room"/>.</summary>
        public static int AffordableCount(long gold, long unitPrice, int room)
        {
            if (gold <= 0 || unitPrice <= 0 || room <= 0) return 0;
            return (int)Math.Min(gold / unitPrice, room);
        }

        /// <summary>
        /// Crystals that cover <paramref name="missingGold"/>: <c>ceil(missing ÷ goldPerCrystal)</c>,
        /// at least 1 — or 0 when nothing is missing.
        /// </summary>
        public static long CrystalsToCover(long missingGold, int goldPerCrystal)
        {
            if (missingGold <= 0) return 0;
            long rate = Math.Max(1, goldPerCrystal);

            // Not (missing + rate − 1) / rate, which overflows near long.MaxValue.
            long crystals = missingGold / rate + (missingGold % rate > 0 ? 1 : 0);
            return Math.Max(1L, crystals);
        }
    }
}
