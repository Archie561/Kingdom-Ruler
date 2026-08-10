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
    /// <para>Time is a stored UTC timestamp plus a rate, never an accumulated delta
    /// (ARCHITECTURE.md §4.5), so a player away for ten hours is caught up in a single
    /// call rather than ten hours of simulated ticks. <see cref="Advance"/> carries the
    /// unused remainder forward so no fraction of a second is lost or double-counted.</para>
    ///
    /// <para>Pure C#: it is handed the current time rather than reading a clock, which is
    /// what makes every timing case directly testable.</para>
    /// </remarks>
    public sealed class ReplenishmentSlots
    {
        private int _count;
        private DateTime _lastCheckUtc;

        public ReplenishmentSlots(DateTime nowUtc)
        {
            _count        = 0;
            _lastCheckUtc = nowUtc;
        }

        /// <summary>How many slots are currently counting down.</summary>
        public int Count => _count;

        /// <summary>When the running countdown last settled. Persisted.</summary>
        public DateTime LastCheckUtc => _lastCheckUtc;

        public bool IsRunning => _count > 0;

        /// <summary>
        /// Begin counting down <paramref name="count"/> slots from scratch.
        /// Ignored if timers are already running, so a redundant fresh-start can't
        /// restart a countdown the player has already partly waited out.
        /// </summary>
        public void StartAll(int count, DateTime nowUtc)
        {
            if (_count > 0 || count <= 0) return;
            _count        = count;
            _lastCheckUtc = nowUtc;
        }

        /// <summary>
        /// Queue one more slot, as when a card is resolved and its slot frees up.
        /// The countdown only restarts if nothing was running — otherwise the slot joins
        /// the back of the existing queue and the in-progress timer is left untouched.
        /// </summary>
        public void AddOne(DateTime nowUtc)
        {
            _count++;
            if (_count == 1) _lastCheckUtc = nowUtc;
        }

        /// <summary>
        /// Settle the countdown up to <paramref name="nowUtc"/>.
        /// Returns how many slots matured, which may be zero.
        /// </summary>
        public int Advance(DateTime nowUtc, float secondsPerSlot)
        {
            if (_count <= 0 || secondsPerSlot <= 0f) return 0;

            // double, not float. With the queue capped at a handful of slots the difference
            // is not observable today — a long absence drains it and resets the clock below
            // — but the arithmetic should not quietly depend on that cap staying small.
            double elapsed = (nowUtc - _lastCheckUtc).TotalSeconds;
            if (elapsed <= 0d) return 0;

            int matured = 0;
            while (elapsed >= secondsPerSlot && _count > 0)
            {
                elapsed -= secondsPerSlot;
                _count--;
                matured++;
            }

            // Carry the remainder so the next slot is not silently restarted from zero.
            // Once nothing is running there is no countdown to carry, and keeping a stale
            // timestamp would persist a nonsensical "last checked" far in the past.
            _lastCheckUtc = _count > 0
                ? nowUtc - TimeSpan.FromSeconds(elapsed)
                : nowUtc;

            return matured;
        }

        /// <summary>
        /// Complete every running timer at once (the crystal instant-refill).
        /// Returns how many were cancelled, which is what the purchase was priced on.
        /// </summary>
        public int CancelAll(DateTime nowUtc)
        {
            int cancelled = _count;
            _count        = 0;
            _lastCheckUtc = nowUtc;
            return cancelled;
        }

        /// <summary>Seconds until the next slot matures; 0 when nothing is running.</summary>
        public float SecondsUntilNext(DateTime nowUtc, float secondsPerSlot)
        {
            if (_count <= 0) return 0f;
            double elapsed = (nowUtc - _lastCheckUtc).TotalSeconds;
            return (float)Math.Max(0d, secondsPerSlot - elapsed);
        }

        /// <summary>Restore from save data. Negative counts are clamped away.</summary>
        public void Restore(int count, DateTime lastCheckUtc)
        {
            _count        = Math.Max(0, count);
            _lastCheckUtc = lastCheckUtc;
        }
    }
}
