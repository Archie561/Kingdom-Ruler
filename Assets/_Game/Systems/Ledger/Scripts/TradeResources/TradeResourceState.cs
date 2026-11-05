using System;

namespace KingdomRuler.Systems.Ledger
{
    /// <summary>
    /// State of one trade resource. Read-only outside <c>KingdomRuler.Systems</c>: every change goes
    /// through <see cref="KingdomLedger"/>.
    /// </summary>
    public sealed class TradeResourceState
    {
        public float Amount { get; internal set; }
        public float Capacity { get; internal set; }
        public float RegenRatePerSecond { get; internal set; }
        public DateTime LastUpdatedUtc { get; internal set; }

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
        /// A baseline in the future <b>resynchronises</b> to <paramref name="now"/>. Nothing else
        /// moves <see cref="LastUpdatedUtc"/> backwards, so ignoring it would leave a warehouse
        /// whose device clock ran ahead and was corrected never regenerating again.
        /// </remarks>
        internal void AccruePassiveRegen(DateTime now)
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
