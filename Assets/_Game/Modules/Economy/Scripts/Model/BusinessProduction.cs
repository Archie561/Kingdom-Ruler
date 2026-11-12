using System;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Economy
{
    /// <summary>
    /// How much a business type produces and stores for a number of units owned (GDD §8). Pure and static.
    /// </summary>
    /// <remarks>
    /// <para><b>This is the one place a production rate is computed.</b> Storage capacity, the per-hour
    /// read-out and the accrual rate all derive from <see cref="GoldPerHour"/>. A production multiplier —
    /// a region bonus (GDD §9), a Shop booster (§11) — is applied there and nowhere else. One owned by
    /// another module will need a shared home in <c>Systems/</c>, since modules cannot call each other,
    /// and a notification so Economy settles its storages before the rate changes.</para>
    ///
    /// <para>Every result saturates at <see cref="KingdomLedger.MaxGold"/> rather than overflowing, so
    /// a mistuned asset degrades to a full-but-capped storage instead of a negative one.
    /// <c>EconomyContentTests</c> keeps the real content inside the ceiling.</para>
    /// </remarks>
    public static class BusinessProduction
    {
        /// <summary>Whole gold per hour from <paramref name="unitsOwned"/> units: <i>n</i> × the base rate.</summary>
        public static long GoldPerHour(BusinessDefinition business, int unitsOwned)
        {
            if (business == null || unitsOwned <= 0) return 0;
            return SaturatingMultiply(Math.Max(1L, business.GoldPerHour), unitsOwned);
        }

        /// <summary>Gold the storage holds when full: <see cref="StorageHours"/> of production.</summary>
        public static long StorageCapacity(BusinessDefinition business, int unitsOwned, EconomyConfig config) =>
            SaturatingMultiply(GoldPerHour(business, unitsOwned), StorageHours(config));

        /// <summary>The time an empty storage takes to fill, at any number of units — the accrual period.</summary>
        public static TimeSpan StoragePeriod(EconomyConfig config) => TimeSpan.FromHours(StorageHours(config));

        private static int StorageHours(EconomyConfig config) => Math.Max(1, config.StorageHours);

        /// <summary>a × b for non-negative values, capped at the gold ceiling.</summary>
        private static long SaturatingMultiply(long a, long b)
        {
            if (a <= 0 || b <= 0) return 0;
            return a > KingdomLedger.MaxGold / b ? KingdomLedger.MaxGold : a * b;
        }
    }
}
