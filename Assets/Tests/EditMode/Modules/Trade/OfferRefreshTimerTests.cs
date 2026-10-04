using System;
using NUnit.Framework;
using KingdomRuler.Modules.Trade.Domain;

namespace KingdomRuler.Tests.EditMode.Modules.Trade
{
    /// <summary>
    /// The 20-minute offer refresh deadline. The Trade analogue of ReplenishmentSlotsTests.
    /// </summary>
    [TestFixture]
    public sealed class OfferRefreshTimerTests
    {
        private const float Interval = 1200f;                       // GDD §7: 20 minutes
        private static readonly DateTime Start = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private static OfferRefreshTimer Started()
        {
            var timer = new OfferRefreshTimer();
            timer.Start(Start, Interval);
            return timer;
        }

        [Test]
        public void Start_SetsTheDeadlineOneIntervalOut()
        {
            var timer = Started();

            Assert.AreEqual(Start.AddSeconds(Interval), timer.NextDueUtc);
            Assert.IsTrue(timer.IsRunning);
        }

        [Test]
        public void Advance_BeforeTheDeadline_DoesNotRefresh()
        {
            var timer = Started();

            Assert.IsFalse(timer.Advance(Start.AddSeconds(Interval - 1), Interval));
        }

        [Test]
        public void Advance_AtTheDeadline_Refreshes()
        {
            var timer = Started();

            Assert.IsTrue(timer.Advance(Start.AddSeconds(Interval), Interval));
        }

        [Test]
        public void Advance_AfterALongAbsence_RefreshesExactlyOnce()
        {
            // Ten hours away is thirty intervals, but each refresh discards the previous offers,
            // so thirty is indistinguishable from one. Simulating them would be pure waste
            // (ARCHITECTURE.md §4.5).
            var timer = Started();
            int refreshes = 0;

            if (timer.Advance(Start.AddHours(10), Interval)) refreshes++;
            // A second settle at the same instant must not fire again.
            if (timer.Advance(Start.AddHours(10), Interval)) refreshes++;

            Assert.AreEqual(1, refreshes);
        }

        [Test]
        public void Advance_PreservesThePhase_RatherThanRestartingAtNow()
        {
            // This is the bug the old code had: it set the timer to `now`, silently handing the
            // player a full fresh interval every time they reopened the app.
            var timer = Started();

            // Return 3.5 intervals after the deadline.
            var now = Start.AddSeconds(Interval + Interval * 3.5f);
            timer.Advance(now, Interval);

            // The next one is due half an interval away, not a full one.
            Assert.AreEqual(Interval * 0.5f, timer.SecondsUntilNext(now), 1f);
        }

        [Test]
        public void Advance_ExactlyOnAnIntervalBoundary_SchedulesAFullIntervalAhead()
        {
            var timer = Started();
            var now = Start.AddSeconds(Interval);

            timer.Advance(now, Interval);

            Assert.AreEqual(Interval, timer.SecondsUntilNext(now), 0.5f);
        }

        [Test]
        public void SecondsUntilNext_IsNeverNegative()
        {
            var timer = Started();

            Assert.AreEqual(0f, timer.SecondsUntilNext(Start.AddHours(5)), 0.001f);
        }

        [Test]
        public void Restore_BringsBackAPersistedDeadline()
        {
            var timer = new OfferRefreshTimer();
            var due   = Start.AddSeconds(300);

            timer.Restore(due, Start, Interval);

            Assert.AreEqual(due, timer.NextDueUtc);
            Assert.AreEqual(300f, timer.SecondsUntilNext(Start), 0.5f);
        }

        [Test]
        public void Restore_WithNoSavedDeadline_StartsAFreshInterval()
        {
            var timer = new OfferRefreshTimer();

            timer.Restore(null, Start, Interval);

            Assert.AreEqual(Start.AddSeconds(Interval), timer.NextDueUtc);
        }

        [Test]
        public void ADeadlineFurtherThanOneIntervalAway_IsPulledBack()
        {
            // The player moved their clock forward, then back. Without this the offer list would
            // be frozen until the original far-future date arrived.
            var timer = new OfferRefreshTimer();

            timer.Restore(Start.AddYears(1), Start, Interval);

            Assert.LessOrEqual(timer.SecondsUntilNext(Start), Interval + 1f);
        }

        [Test]
        public void ABackwardsClock_DoesNotLockTheTimerOut()
        {
            var timer = Started();

            // "Now" jumps backwards a long way.
            Assert.DoesNotThrow(() => timer.Advance(Start.AddYears(-1), Interval));
            Assert.LessOrEqual(timer.SecondsUntilNext(Start.AddYears(-1)), Interval + 1f);
        }

        [Test]
        public void Advance_OnAnUnstartedTimer_StartsItWithoutRefreshing()
        {
            var timer = new OfferRefreshTimer();

            Assert.IsFalse(timer.Advance(Start, Interval));
            Assert.IsTrue(timer.IsRunning);
        }

        [Test]
        public void ANonPositiveInterval_DoesNotHangOrDivideByZero()
        {
            var timer = new OfferRefreshTimer();
            timer.Start(Start, 0f);

            Assert.DoesNotThrow(() => timer.Advance(Start.AddHours(1), 0f));
            Assert.IsTrue(timer.IsRunning);
        }
    }
}
