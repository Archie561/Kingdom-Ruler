using System;

namespace KingdomRuler.Modules.Laws
{
    /// <summary>
    /// Counts down the cards being replenished: one card matures per interval, one after another.
    /// </summary>
    /// <remarks>
    /// The state is an absolute deadline, not a remaining duration, so the timer survives the app
    /// being closed: whenever it is next advanced it compares against <c>now</c>, and any absence
    /// settles in one call. The deadline advances in whole intervals, so partial progress toward
    /// the next card is never lost.
    /// </remarks>
    public sealed class ReplenishmentTimer
    {
        /// <summary>When the next card matures. Null exactly when nothing is pending.</summary>
        private DateTime? _nextDueUtc;

        /// <summary>How many cards are still counting down.</summary>
        public int PendingCount { get; private set; }

        public DateTime? NextDueUtc => _nextDueUtc;
        public bool IsRunning => PendingCount > 0;

        /// <summary>
        /// Start counting down <paramref name="count"/> cards. Ignored while already running, so a
        /// redundant start cannot reset a countdown the player has partly waited out.
        /// </summary>
        public void Start(int count, DateTime nowUtc, float interval)
        {
            if (IsRunning || count <= 0 || interval <= 0f) return;
            PendingCount = count;
            _nextDueUtc  = nowUtc.AddSeconds(interval);
        }

        /// <summary>Queue one more card. Joins the back of the line; the running countdown is untouched.</summary>
        public void AddOne(DateTime nowUtc, float interval)
        {
            PendingCount++;
            if (PendingCount == 1 && interval > 0f)
                _nextDueUtc = nowUtc.AddSeconds(interval);
        }

        /// <summary>Settle the countdown up to <paramref name="nowUtc"/>. Returns how many cards matured.</summary>
        public int Advance(DateTime nowUtc, float interval)
        {
            if (!IsRunning || interval <= 0f || !_nextDueUtc.HasValue) return 0;

            // A deadline is never more than one interval away. If it is, the device clock was moved
            // back (or the save was edited); pull it in rather than stall the queue for that long.
            var latestDue = nowUtc.AddSeconds(interval);
            if (_nextDueUtc.Value > latestDue) _nextDueUtc = latestDue;

            int matured = 0;
            while (PendingCount > 0 && nowUtc >= _nextDueUtc.Value)
            {
                PendingCount--;
                matured++;
                _nextDueUtc = _nextDueUtc.Value.AddSeconds(interval);
            }

            if (PendingCount == 0) _nextDueUtc = null;
            return matured;
        }

        /// <summary>Mature every pending card at once.</summary>
        public void CompleteAll()
        {
            PendingCount = 0;
            _nextDueUtc  = null;
        }

        public float SecondsUntilNext(DateTime nowUtc)
        {
            if (!IsRunning || !_nextDueUtc.HasValue) return 0f;
            return (float)Math.Max(0d, (_nextDueUtc.Value - nowUtc).TotalSeconds);
        }

        /// <summary>Seconds until every pending card has matured.</summary>
        public float SecondsUntilAll(DateTime nowUtc, float interval) =>
            IsRunning ? SecondsUntilNext(nowUtc) + (PendingCount - 1) * interval : 0f;

        /// <summary>Restore from save data. A non-positive count drops the deadline with it.</summary>
        public void Restore(int pendingCount, DateTime nextDueUtc)
        {
            PendingCount = Math.Max(0, pendingCount);
            _nextDueUtc  = IsRunning ? nextDueUtc : (DateTime?)null;
        }
    }
}
