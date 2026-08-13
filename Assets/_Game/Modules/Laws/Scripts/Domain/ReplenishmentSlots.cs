using System;

namespace KingdomRuler.Modules.Laws.Domain
{
    /// <summary>
    /// The law queue's replenishment timers: how many slots are counting down, and when
    /// the next one matures (GDD §6 — one card every two minutes, real time).
    /// </summary>
    /// <remarks>
    /// <para>Slots mature <b>sequentially</b>, not in parallel: one shared countdown, and
    /// each time it elapses exactly one slot completes. Eight empty slots therefore take
    /// eight intervals to refill, not one.</para>
    ///
    /// <para><b>State is an absolute due time, never a remaining duration.</b> A countdown
    /// that decrements needs something running to decrement it, so it cannot survive the
    /// app closing. A wall-clock deadline can: whenever the game next looks, it subtracts
    /// <c>now</c> and gets the right answer regardless of how long it was away
    /// (ARCHITECTURE.md §4.5). A player gone ten hours is caught up in one call rather than
    /// ten hours of simulated ticks.</para>
    ///
    /// <para>Storing the deadline rather than the last-checked time also means a partial
    /// interval survives for free — the deadline simply marches forward in whole intervals
    /// and any unelapsed remainder is still ahead of <c>now</c>.</para>
    ///
    /// <para>Pure C#: it is handed the current time rather than reading a clock, which is
    /// what makes every timing case directly testable.</para>
    /// </remarks>
    public sealed class ReplenishmentSlots
    {
        private int _count;

        /// <summary>When the next slot matures. Null exactly when nothing is running.</summary>
        private DateTime? _nextDueUtc;

        /// <summary>How many slots are currently counting down.</summary>
        public int Count => _count;

        /// <summary>
        /// Wall-clock time the next slot matures, or null when nothing is running.
        /// This is the value that gets persisted.
        /// </summary>
        public DateTime? NextDueUtc => _nextDueUtc;

        public bool IsRunning => _count > 0;

        /// <summary>
        /// Begin counting down <paramref name="count"/> slots from scratch.
        /// Ignored if timers are already running, so a redundant fresh-start can't
        /// restart a countdown the player has already partly waited out.
        /// </summary>
        public void StartAll(int count, DateTime nowUtc, float secondsPerSlot)
        {
            if (_count > 0 || count <= 0 || secondsPerSlot <= 0f) return;
            _count      = count;
            _nextDueUtc = nowUtc.AddSeconds(secondsPerSlot);
        }

        /// <summary>
        /// Queue one more slot, as when a card is resolved and its slot frees up.
        /// A countdown only starts if nothing was running — otherwise the slot joins the
        /// back of the queue and the in-progress deadline is left untouched.
        /// </summary>
        public void AddOne(DateTime nowUtc, float secondsPerSlot)
        {
            _count++;
            if (_count == 1 && secondsPerSlot > 0f)
                _nextDueUtc = nowUtc.AddSeconds(secondsPerSlot);
        }

        /// <summary>
        /// Settle the countdown up to <paramref name="nowUtc"/>.
        /// Returns how many slots matured, which may be zero.
        /// </summary>
        public int Advance(DateTime nowUtc, float secondsPerSlot)
        {
            if (_count <= 0 || secondsPerSlot <= 0f || !_nextDueUtc.HasValue) return 0;

            int matured = 0;
            while (_count > 0 && nowUtc >= _nextDueUtc.Value)
            {
                _count--;
                matured++;
                // Step the deadline by a whole interval rather than restarting it from
                // now: that is what preserves the unelapsed remainder.
                _nextDueUtc = _nextDueUtc.Value.AddSeconds(secondsPerSlot);
            }

            if (_count == 0) _nextDueUtc = null;
            return matured;
        }

        /// <summary>
        /// Complete every running timer at once (the crystal instant-refill).
        /// Returns how many were cancelled, which is what the purchase was priced on.
        /// </summary>
        public int CancelAll()
        {
            int cancelled = _count;
            _count        = 0;
            _nextDueUtc   = null;
            return cancelled;
        }

        /// <summary>Seconds until the next slot matures; 0 when nothing is running.</summary>
        public float SecondsUntilNext(DateTime nowUtc)
        {
            if (_count <= 0 || !_nextDueUtc.HasValue) return 0f;
            return (float)Math.Max(0d, (_nextDueUtc.Value - nowUtc).TotalSeconds);
        }

        /// <summary>
        /// Restore from save data. A non-positive count is clamped away, and the deadline
        /// is dropped with it — the two must never disagree.
        /// </summary>
        public void Restore(int count, DateTime nextDueUtc)
        {
            _count      = Math.Max(0, count);
            _nextDueUtc = _count > 0 ? nextDueUtc : (DateTime?)null;
        }
    }
}
