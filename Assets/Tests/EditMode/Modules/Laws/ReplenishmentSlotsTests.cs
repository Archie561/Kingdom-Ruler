using System;
using NUnit.Framework;
using KingdomRuler.Modules.Laws.Domain;

namespace KingdomRuler.Tests.EditMode.Modules.Laws
{
    /// <summary>
    /// Timer arithmetic in isolation. These cases were previously only reachable by
    /// driving the whole manager with a fake clock.
    /// </summary>
    [TestFixture]
    public sealed class ReplenishmentSlotsTests
    {
        private const float Interval = 120f;
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static ReplenishmentSlots WithRunning(int count)
        {
            var slots = new ReplenishmentSlots();
            slots.StartAll(count, T0, Interval);
            return slots;
        }

        // ── Advance ───────────────────────────────────────────────────────────────

        [Test]
        public void Advance_WithNothingRunning_MaturesNothing()
        {
            var slots = new ReplenishmentSlots();
            Assert.AreEqual(0, slots.Advance(T0.AddHours(5), Interval));
        }

        [Test]
        public void Advance_BelowOneInterval_MaturesNothing()
        {
            var slots = WithRunning(8);
            Assert.AreEqual(0, slots.Advance(T0.AddSeconds(Interval - 1), Interval));
            Assert.AreEqual(8, slots.Count);
        }

        [Test]
        public void Advance_ExactlyOneInterval_MaturesOne()
        {
            var slots = WithRunning(8);
            Assert.AreEqual(1, slots.Advance(T0.AddSeconds(Interval), Interval));
            Assert.AreEqual(7, slots.Count);
        }

        [Test]
        public void Advance_SeveralIntervals_MaturesOnePerInterval()
        {
            var slots = WithRunning(8);
            Assert.AreEqual(3, slots.Advance(T0.AddSeconds(Interval * 3), Interval));
            Assert.AreEqual(5, slots.Count);
        }

        [Test]
        public void Advance_MoreIntervalsThanSlots_MaturesOnlyWhatExists()
        {
            var slots = WithRunning(3);
            Assert.AreEqual(3, slots.Advance(T0.AddDays(30), Interval));
            Assert.AreEqual(0, slots.Count);
        }

        [Test]
        public void Advance_BackwardsClock_MaturesNothing()
        {
            var slots = WithRunning(4);
            Assert.AreEqual(0, slots.Advance(T0.AddSeconds(-500), Interval));
            Assert.AreEqual(4, slots.Count);
        }

        [Test]
        public void Advance_NonPositiveInterval_MaturesNothingRatherThanSpinning()
        {
            var slots = WithRunning(4);
            Assert.AreEqual(0, slots.Advance(T0.AddDays(1), 0f));
            Assert.AreEqual(4, slots.Count);
        }

        /// <summary>
        /// Partial progress toward the next slot must survive the call, or a player
        /// checking in frequently would reset the countdown every time and never get a card.
        /// </summary>
        [Test]
        public void Advance_CarriesTheRemainderForward()
        {
            var slots = WithRunning(5);

            Assert.AreEqual(2, slots.Advance(T0.AddSeconds(Interval * 2 + 10), Interval));

            // 10 seconds of the next interval are already served.
            Assert.AreEqual(Interval - 10f,
                slots.SecondsUntilNext(T0.AddSeconds(Interval * 2 + 10)), 0.01f);
        }

        [Test]
        public void Advance_RepeatedSmallSteps_MatchOneBigStep()
        {
            var stepped = WithRunning(8);
            int maturedStepwise = 0;
            for (int i = 1; i <= 600; i++)                       // 600 × 1s = 5 intervals
                maturedStepwise += stepped.Advance(T0.AddSeconds(i), Interval);

            var atOnce = WithRunning(8);
            int maturedAtOnce = atOnce.Advance(T0.AddSeconds(600), Interval);

            Assert.AreEqual(maturedAtOnce, maturedStepwise);
            Assert.AreEqual(atOnce.Count, stepped.Count);
        }

        /// <summary>
        /// Once nothing is running there is no countdown to carry. Leaving the old
        /// timestamp would persist a "last checked" far in the past.
        /// </summary>
        [Test]
        public void Advance_WhenLastSlotMatures_TimestampSettlesAtNow()
        {
            var slots = WithRunning(1);
            var now = T0.AddDays(10);

            slots.Advance(now, Interval);

            Assert.AreEqual(0, slots.Count);
            Assert.IsFalse(slots.NextDueUtc.HasValue,
                "Nothing is running, so there is no deadline to hold.");
        }

        /// <summary>
        /// A months-long absence drains the queue and must leave a sane timestamp behind.
        /// The old code carried the entire unused remainder forward, persisting a
        /// "last checked" months in the past — which then made the next countdown nonsense.
        /// </summary>
        [Test]
        public void Advance_AfterAVeryLongAbsence_DrainsTheQueueAndSettlesTheClock()
        {
            var slots = WithRunning(8);
            var now = T0.AddDays(200).AddSeconds(37);

            Assert.AreEqual(8, slots.Advance(now, Interval));
            Assert.AreEqual(0, slots.Count);
            Assert.IsFalse(slots.NextDueUtc.HasValue,
                "No countdown is running, so there is no deadline to hold.");

            // A slot queued after the absence must get a full, correct interval.
            slots.AddOne(now, Interval);
            Assert.AreEqual(Interval, slots.SecondsUntilNext(now), 0.01f);
        }

        [Test]
        public void Advance_PartialDrain_KeepsTheRemainderAccurate()
        {
            // 8 slots, but only 3 intervals plus 37s have passed.
            var slots = WithRunning(8);
            var now = T0.AddSeconds(Interval * 3 + 37);

            Assert.AreEqual(3, slots.Advance(now, Interval));
            Assert.AreEqual(5, slots.Count);
            Assert.AreEqual(Interval - 37f, slots.SecondsUntilNext(now), 0.01f);
        }

        // ── StartAll / AddOne ─────────────────────────────────────────────────────

        [Test]
        public void StartAll_WhileAlreadyRunning_DoesNotRestartTheCountdown()
        {
            var slots = WithRunning(8);
            slots.Advance(T0.AddSeconds(60), Interval);          // 60s of progress

            slots.StartAll(8, T0.AddSeconds(60), Interval);

            Assert.AreEqual(Interval - 60f,
                slots.SecondsUntilNext(T0.AddSeconds(60)), 0.01f,
                "A redundant fresh start must not discard progress the player already waited out.");
        }

        [Test]
        public void StartAll_WithNonPositiveCount_DoesNothing()
        {
            var slots = new ReplenishmentSlots();
            slots.StartAll(0, T0, Interval);
            Assert.IsFalse(slots.IsRunning);
        }

        [Test]
        public void AddOne_WhenIdle_StartsTheCountdownFromNow()
        {
            var slots = new ReplenishmentSlots();
            var now = T0.AddHours(3);

            slots.AddOne(now, Interval);

            Assert.AreEqual(1, slots.Count);
            Assert.AreEqual(Interval, slots.SecondsUntilNext(now), 0.01f);
        }

        [Test]
        public void AddOne_WhileRunning_LeavesTheInProgressTimerAlone()
        {
            var slots = WithRunning(2);
            var now = T0.AddSeconds(60);

            slots.AddOne(now, Interval);

            Assert.AreEqual(3, slots.Count);
            Assert.AreEqual(Interval - 60f, slots.SecondsUntilNext(now), 0.01f,
                "Queuing another slot must not reset the countdown already in progress.");
        }

        // ── CancelAll / SecondsUntilNext / Restore ────────────────────────────────

        [Test]
        public void CancelAll_ReturnsHowManyWereRunningAndStopsThem()
        {
            var slots = WithRunning(6);

            Assert.AreEqual(6, slots.CancelAll());
            Assert.AreEqual(0, slots.Count);
            Assert.IsFalse(slots.IsRunning);
        }

        [Test]
        public void SecondsUntilNext_WithNothingRunning_IsZero()
        {
            Assert.AreEqual(0f, new ReplenishmentSlots().SecondsUntilNext(T0.AddDays(1)));
        }

        [Test]
        public void SecondsUntilNext_NeverGoesNegative()
        {
            var slots = WithRunning(2);
            Assert.AreEqual(0f, slots.SecondsUntilNext(T0.AddSeconds(Interval * 10)));
        }

        [Test]
        public void Restore_ClampsNegativeCountToZero()
        {
            var slots = new ReplenishmentSlots();
            slots.Restore(-5, T0);
            Assert.AreEqual(0, slots.Count);
        }

        [Test]
        public void Restore_RoundTripsCountAndDeadline()
        {
            var slots = new ReplenishmentSlots();
            var due = T0.AddMinutes(17);

            slots.Restore(4, due);

            Assert.AreEqual(4, slots.Count);
            Assert.AreEqual(due, slots.NextDueUtc);
        }

        [Test]
        public void Restore_WithZeroCount_DropsTheDeadlineToo()
        {
            var slots = new ReplenishmentSlots();
            slots.Restore(0, T0.AddMinutes(5));

            Assert.AreEqual(0, slots.Count);
            Assert.IsFalse(slots.NextDueUtc.HasValue,
                "A deadline with nothing pending is a contradiction.");
        }

        [Test]
        public void NextDueUtc_IsExactlyOneIntervalAfterStart()
        {
            var slots = WithRunning(3);
            Assert.AreEqual(T0.AddSeconds(Interval), slots.NextDueUtc);
        }
    }
}
