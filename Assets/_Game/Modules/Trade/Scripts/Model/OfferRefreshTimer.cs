using System;

namespace KingdomRuler.Modules.Trade
{
    /// <summary>When the offer list is next replaced (GDD §7: every 20 minutes). Pure: handed the time.</summary>
    /// <remarks>
    /// <para>It stores an absolute deadline, not a remaining duration, so it survives the app being
    /// closed: whenever it is next advanced it compares against <c>now</c>.</para>
    ///
    /// <para><b>One refresh per settle, however long the absence.</b> Each refresh discards the
    /// previous offers, so ten hours away (thirty intervals) is indistinguishable from one refresh.
    /// The deadline still advances by <i>whole intervals</i>, so the phase survives: return 3.5
    /// intervals late and the next refresh is half an interval away, not a full one.</para>
    ///
    /// <para><b>The deadline is never more than one interval away</b> (<c>ARCHITECTURE.md</c> §4.5):
    /// a clock moved back, or an edited save, would otherwise freeze the list until that date.</para>
    /// </remarks>
    public sealed class OfferRefreshTimer
    {
        /// <summary>Used when the interval given is not positive, so the timer keeps moving; <c>TradeConfig.OnValidate</c> stops it being authored.</summary>
        private const float FallbackIntervalSeconds = 1200f;

        /// <summary>When the next refresh falls due, or null before the timer is first started.</summary>
        public DateTime? NextDueUtc { get; private set; }

        /// <summary>Begin a fresh interval ending one interval from <paramref name="now"/>.</summary>
        public void Start(DateTime now, float intervalSeconds) =>
            NextDueUtc = now.AddSeconds(Sanitize(intervalSeconds));

        /// <summary>Restore a saved deadline. A missing one starts a fresh interval; one too far away is pulled in.</summary>
        public void Restore(DateTime? dueUtc, DateTime now, float intervalSeconds)
        {
            if (!dueUtc.HasValue)
            {
                Start(now, intervalSeconds);
                return;
            }

            NextDueUtc = dueUtc.Value;
            PullIn(now, Sanitize(intervalSeconds));
        }

        /// <summary>
        /// Settle against <paramref name="now"/>. True when the list should be replaced — at most
        /// once per call, however far past the deadline.
        /// </summary>
        public bool Advance(DateTime now, float intervalSeconds)
        {
            float interval = Sanitize(intervalSeconds);

            if (!NextDueUtc.HasValue)
            {
                Start(now, interval);
                return false;
            }

            PullIn(now, interval);

            var due = NextDueUtc.Value;
            if (now < due) return false;

            // Whole intervals, so the phase survives: 3.5 intervals overdue skips 4.
            double intervalsToSkip = Math.Floor((now - due).TotalSeconds / interval) + 1;
            NextDueUtc = due.AddSeconds(intervalsToSkip * interval);
            return true;
        }

        /// <summary>Seconds until the next refresh; zero when due or unset.</summary>
        public float SecondsUntilNext(DateTime now)
        {
            if (!NextDueUtc.HasValue) return 0f;
            double remaining = (NextDueUtc.Value - now).TotalSeconds;
            return remaining > 0d ? (float)remaining : 0f;
        }

        private void PullIn(DateTime now, float interval)
        {
            var latest = now.AddSeconds(interval);
            if (NextDueUtc.Value > latest) NextDueUtc = latest;
        }

        private static float Sanitize(float intervalSeconds) =>
            intervalSeconds > 0f ? intervalSeconds : FallbackIntervalSeconds;
    }
}
