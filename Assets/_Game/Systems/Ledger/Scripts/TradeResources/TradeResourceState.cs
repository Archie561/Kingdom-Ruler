using System;

namespace KingdomRuler.Systems.Ledger
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
        /// Fast-forward passive regeneration from <see cref="LastUpdatedUtc"/> to the given time.
        /// </summary>
        /// <remarks>
        /// A baseline in the future <b>resynchronises</b> rather than being ignored. Returning
        /// early instead would strand the resource permanently: nothing else ever moves
        /// <see cref="LastUpdatedUtc"/> backwards, so a device clock that ran ahead and was later
        /// corrected would stop that warehouse regenerating for good. Resyncing costs the player
        /// only the drift itself.
        /// </remarks>
        public void AccruePassiveRegen(DateTime now)
        {
            if (now <= LastUpdatedUtc)
            {
                LastUpdatedUtc = now;
                return;
            }

            var elapsed = (float)(now - LastUpdatedUtc).TotalSeconds;
            Amount = Math.Min(Amount + elapsed * RegenRatePerSecond, Capacity);
            LastUpdatedUtc = now;
        }
    }
}
