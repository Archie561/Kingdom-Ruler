using System;

namespace KingdomRuler.Modules.Economy.Domain
{
    public static class BusinessAccrualCalculator
    {
        /// <summary>Storage cap = total production per minute * 1440 (24 hours).</summary>
        public static long StorageCap(int countOwned, long baseGoldPerMinute)
        {
            return (long)countOwned * baseGoldPerMinute * 1440L;
        }

        /// <summary>
        /// Calculate gold accrued since last check. O(1), no tick simulation.
        /// Returns the new stored gold value (capped at storage cap).
        /// </summary>
        public static long Accrue(long currentStored, int countOwned, long baseGoldPerMinute,
            DateTime lastAccruedUtc, DateTime nowUtc)
        {
            if (countOwned <= 0) return currentStored;
            var elapsedMinutes = (nowUtc - lastAccruedUtc).TotalMinutes;
            if (elapsedMinutes <= 0) return currentStored;

            long totalRate = (long)countOwned * baseGoldPerMinute;
            long accrued = (long)(elapsedMinutes * totalRate);
            long cap = StorageCap(countOwned, baseGoldPerMinute);
            return Math.Min(cap, currentStored + accrued);
        }
    }
}
