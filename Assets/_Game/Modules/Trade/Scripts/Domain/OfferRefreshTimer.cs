using System;

namespace KingdomRuler.Modules.Trade.Domain
{
    /// <summary>
    /// When the trade offer list is next due to be replaced (<c>GDD.md</c> §7 — every 20
    /// minutes). Pure arithmetic: it is handed the current time rather than reading a clock, so
    /// it is directly unit-testable.
    /// </summary>
    /// <remarks>
    /// <para><b>Stores an absolute deadline, not a remaining duration</b> — the same decision
    /// <c>ReplenishmentSlots</c> made for Laws, and the change Laws' save made in schema v4. A
    /// countdown that decrements needs something running to decrement it, so it cannot survive
    /// the app closing; a wall-clock deadline can. Subtract <c>now</c> whenever you next look and
    /// the answer is right regardless of how long you were away.</para>
    ///
    /// <para><b>One refresh per settle, however long the absence.</b> Ten hours away does not
    /// mean thirty refreshes — each refresh discards the previous offers, so thirty is
    /// indistinguishable from one and simulating them would be pure waste
    /// (<c>ARCHITECTURE.md</c> §4.5). But the deadline still advances by <em>whole intervals</em>
    /// rather than restarting at <c>now</c>, so the refresh phase is preserved: return 3.5
    /// intervals late and the next one is due in half an interval, not a full one. The old code
    /// reset the timer to the moment of load, which silently gave the player a full fresh
    /// interval every time they reopened the app.</para>
    /// </remarks>
    public sealed class OfferRefreshTimer
    {
        /// <summary>When the next refresh falls due, or null if the timer has never started.</summary>
        public DateTime? NextDueUtc { get; private set; }

        /// <summary>Whether a deadline is set.</summary>
        public bool IsRunning => NextDueUtc.HasValue;

        /// <summary>Begin a fresh interval ending one interval from <paramref name="now"/>.</summary>
        public void Start(DateTime now, float intervalSeconds)
        {
            NextDueUtc = now.AddSeconds(SanitizeInterval(intervalSeconds));
        }

        /// <summary>
        /// Restore a persisted deadline. A null or already-absurd value falls back to a fresh
        /// interval, so a corrupt save cannot strand the timer.
        /// </summary>
        public void Restore(DateTime? dueUtc, DateTime now, float intervalSeconds)
        {
            if (!dueUtc.HasValue) { Start(now, intervalSeconds); return; }

            NextDueUtc = dueUtc.Value;
            GuardAgainstBackwardsClock(now, intervalSeconds);
        }

        /// <summary>
        /// Settle the timer against the current time. Returns true when the offer list should be
        /// replaced — at most once per call, however far past the deadline we are.
        /// </summary>
        public bool Advance(DateTime now, float intervalSeconds)
        {
            float interval = SanitizeInterval(intervalSeconds);

            if (!NextDueUtc.HasValue) { Start(now, interval); return false; }

            GuardAgainstBackwardsClock(now, interval);

            var due = NextDueUtc.Value;
            if (now < due) return false;

            // Advance by whole intervals so the phase survives a long absence. Overdue by 3.5
            // intervals → skip 4 → the next one lands half an interval from now.
            double overdueSeconds = (now - due).TotalSeconds;
            double intervalsToSkip = Math.Floor(overdueSeconds / interval) + 1;
            NextDueUtc = due.AddSeconds(intervalsToSkip * interval);

            return true;
        }

        /// <summary>Seconds until the next refresh, never negative. Zero when overdue or unset.</summary>
        public float SecondsUntilNext(DateTime now)
        {
            if (!NextDueUtc.HasValue) return 0f;
            double remaining = (NextDueUtc.Value - now).TotalSeconds;
            return remaining > 0d ? (float)remaining : 0f;
        }

        /// <summary>
        /// A deadline more than one interval away cannot have been set legitimately, so the
        /// player's clock moved backwards (or the save was edited). Pull it back to one interval
        /// out rather than leaving the offer list frozen until the original date arrives.
        /// </summary>
        private void GuardAgainstBackwardsClock(DateTime now, float intervalSeconds)
        {
            if (!NextDueUtc.HasValue) return;

            var latestLegitimate = now.AddSeconds(intervalSeconds);
            if (NextDueUtc.Value > latestLegitimate) NextDueUtc = latestLegitimate;
        }

        /// <summary>
        /// A non-positive interval would make <c>Advance</c> divide by zero and the deadline
        /// never move. Falling back keeps the game running; the config's own OnValidate is what
        /// stops it being authored that way.
        /// </summary>
        private static float SanitizeInterval(float intervalSeconds) =>
            intervalSeconds > 0f ? intervalSeconds : 1200f;
    }
}
