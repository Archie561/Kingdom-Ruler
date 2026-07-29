using System;

namespace KingdomRuler.Shared.Ledger
{
    /// <summary>
    /// Mutable state for a single trade resource. Owned exclusively by KingdomLedger.
    /// </summary>
    public sealed class TradeResourceState
    {
        public float Amount { get; set; }
        public float Capacity { get; set; }
        public float RegenRatePerSecond { get; set; }
        public DateTime LastUpdatedUtc { get; set; }

        public TradeResourceState(float capacity, float regenRatePerSecond)
        {
            Amount = 0f;
            Capacity = capacity;
            RegenRatePerSecond = regenRatePerSecond;
            LastUpdatedUtc = DateTime.UtcNow;
        }

        /// <summary>
        /// Fast-forward passive regeneration from lastUpdatedUtc to the given time.
        /// </summary>
        public void AccruePassiveRegen(DateTime now)
        {
            if (now <= LastUpdatedUtc) return;
            var elapsed = (float)(now - LastUpdatedUtc).TotalSeconds;
            Amount = Math.Min(Amount + elapsed * RegenRatePerSecond, Capacity);
            LastUpdatedUtc = now;
        }
    }
}
