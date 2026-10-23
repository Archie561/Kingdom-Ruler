using System;
using NUnit.Framework;
using KingdomRuler.Modules.Laws;

namespace KingdomRuler.Tests.EditMode.Modules.Laws
{
    [TestFixture]
    public sealed class ReplenishmentTimerTests
    {
        private const float Interval = 120f;
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static ReplenishmentTimer Running(int count)
        {
            var timer = new ReplenishmentTimer();
            timer.Start(count, T0, Interval);
            return timer;
        }

        // ── Advance ───────────────────────────────────────────────────────────────

        [Test]
        public void Advance_WithNothingPending_MaturesNothing()
        {
            Assert.AreEqual(0, new ReplenishmentTimer().Advance(T0.AddHours(5), Interval));
        }

        [Test]
        public void Advance_BelowOneInterval_MaturesNothing()
        {
            var timer = Running(8);
            Assert.AreEqual(0, timer.Advance(T0.AddSeconds(Interval - 1), Interval));
            Assert.AreEqual(8, timer.PendingCount);
        }

        [Test]
        public void Advance_ExactlyOneInterval_MaturesOne()
        {
            var timer = Running(8);
            Assert.AreEqual(1, timer.Advance(T0.AddSeconds(Interval), Interval));
            Assert.AreEqual(7, timer.PendingCount);
        }

        [Test]
        public void Advance_SeveralIntervals_MaturesOnePerInterval()
        {
            var timer = Running(8);
            Assert.AreEqual(3, timer.Advance(T0.AddSeconds(Interval * 3), Interval));
            Assert.AreEqual(5, timer.PendingCount);
        }

        [Test]
        public void Advance_MoreIntervalsThanPending_MaturesOnlyWhatIsPending()
        {
            var timer = Running(3);
            Assert.AreEqual(3, timer.Advance(T0.AddDays(30), Interval));
            Assert.AreEqual(0, timer.PendingCount);
        }

        [Test]
        public void Advance_BackwardsClock_MaturesNothing()
        {
            var timer = Running(4);
            Assert.AreEqual(0, timer.Advance(T0.AddSeconds(-500), Interval));
            Assert.AreEqual(4, timer.PendingCount);
        }

        /// <summary>The device clock moved back by a day: the next card is one interval away, not a day.</summary>
        [Test]
        public void Advance_DeadlineMoreThanAnIntervalAway_IsPulledInToOneInterval()
        {
            var timer = Running(4);
            var clockMovedBack = T0.AddDays(-1);

            Assert.AreEqual(0, timer.Advance(clockMovedBack, Interval));
            Assert.AreEqual(Interval, timer.SecondsUntilNext(clockMovedBack), 0.01f);
            Assert.AreEqual(1, timer.Advance(clockMovedBack.AddSeconds(Interval), Interval));
        }

        [Test]
        public void Advance_DeadlineWithinOneInterval_IsLeftAlone()
        {
            var timer = Running(4);
            var now = T0.AddSeconds(30);

            timer.Advance(now, Interval);

            Assert.AreEqual(T0.AddSeconds(Interval), timer.NextDueUtc);
        }

        [Test]
        public void Advance_NonPositiveInterval_MaturesNothingRatherThanSpinning()
        {
            var timer = Running(4);
            Assert.AreEqual(0, timer.Advance(T0.AddDays(1), 0f));
            Assert.AreEqual(4, timer.PendingCount);
        }

        /// <summary>A player checking in often must not reset the countdown every time.</summary>
        [Test]
        public void Advance_CarriesTheRemainderForward()
        {
            var timer = Running(5);
            var now = T0.AddSeconds(Interval * 2 + 10);

            Assert.AreEqual(2, timer.Advance(now, Interval));
            Assert.AreEqual(Interval - 10f, timer.SecondsUntilNext(now), 0.01f);
        }

        [Test]
        public void Advance_RepeatedSmallSteps_MatchOneBigStep()
        {
            var stepped = Running(8);
            int maturedStepwise = 0;
            for (int i = 1; i <= 600; i++)
                maturedStepwise += stepped.Advance(T0.AddSeconds(i), Interval);

            var atOnce = Running(8);
            int maturedAtOnce = atOnce.Advance(T0.AddSeconds(600), Interval);

            Assert.AreEqual(maturedAtOnce, maturedStepwise);
            Assert.AreEqual(atOnce.PendingCount, stepped.PendingCount);
        }

        [Test]
        public void Advance_WhenTheLastCardMatures_DropsTheDeadline()
        {
            var timer = Running(1);
            timer.Advance(T0.AddDays(10), Interval);

            Assert.AreEqual(0, timer.PendingCount);
            Assert.IsFalse(timer.NextDueUtc.HasValue);
        }

        [Test]
        public void Advance_AfterAVeryLongAbsence_LeavesASaneTimerForTheNextCard()
        {
            var timer = Running(8);
            var now = T0.AddDays(200).AddSeconds(37);

            Assert.AreEqual(8, timer.Advance(now, Interval));
            Assert.IsFalse(timer.NextDueUtc.HasValue);

            timer.AddOne(now, Interval);
            Assert.AreEqual(Interval, timer.SecondsUntilNext(now), 0.01f);
        }

        // ── Start / AddOne ────────────────────────────────────────────────────────

        [Test]
        public void Start_WhileRunning_DoesNotRestartTheCountdown()
        {
            var timer = Running(8);
            var now = T0.AddSeconds(60);

            timer.Start(8, now, Interval);

            Assert.AreEqual(Interval - 60f, timer.SecondsUntilNext(now), 0.01f);
        }

        [Test]
        public void Start_WithNonPositiveCount_DoesNothing()
        {
            var timer = new ReplenishmentTimer();
            timer.Start(0, T0, Interval);
            Assert.IsFalse(timer.IsRunning);
        }

        [Test]
        public void NextDueUtc_IsOneIntervalAfterStart()
        {
            Assert.AreEqual(T0.AddSeconds(Interval), Running(3).NextDueUtc);
        }

        [Test]
        public void AddOne_WhenIdle_StartsTheCountdownFromNow()
        {
            var timer = new ReplenishmentTimer();
            var now = T0.AddHours(3);

            timer.AddOne(now, Interval);

            Assert.AreEqual(1, timer.PendingCount);
            Assert.AreEqual(Interval, timer.SecondsUntilNext(now), 0.01f);
        }

        [Test]
        public void AddOne_WhileRunning_LeavesTheCountdownAlone()
        {
            var timer = Running(2);
            var now = T0.AddSeconds(60);

            timer.AddOne(now, Interval);

            Assert.AreEqual(3, timer.PendingCount);
            Assert.AreEqual(Interval - 60f, timer.SecondsUntilNext(now), 0.01f);
        }

        // ── CompleteAll / queries / Restore ───────────────────────────────────────

        [Test]
        public void CompleteAll_StopsEverything()
        {
            var timer = Running(6);
            timer.CompleteAll();

            Assert.AreEqual(0, timer.PendingCount);
            Assert.IsFalse(timer.NextDueUtc.HasValue);
        }

        [Test]
        public void SecondsUntilNext_WithNothingPending_IsZero()
        {
            Assert.AreEqual(0f, new ReplenishmentTimer().SecondsUntilNext(T0));
        }

        [Test]
        public void SecondsUntilNext_NeverGoesNegative()
        {
            Assert.AreEqual(0f, Running(2).SecondsUntilNext(T0.AddSeconds(Interval * 10)));
        }

        [Test]
        public void SecondsUntilAll_IsTheNextDeadlinePlusAWholeIntervalPerCardBehindIt()
        {
            var timer = Running(3);
            Assert.AreEqual(Interval * 3 - 20f, timer.SecondsUntilAll(T0.AddSeconds(20), Interval), 0.01f);
            Assert.AreEqual(0f, new ReplenishmentTimer().SecondsUntilAll(T0, Interval));
        }

        [Test]
        public void Restore_RoundTripsCountAndDeadline()
        {
            var timer = new ReplenishmentTimer();
            var due = T0.AddMinutes(17);

            timer.Restore(4, due);

            Assert.AreEqual(4, timer.PendingCount);
            Assert.AreEqual(due, timer.NextDueUtc);
        }

        [Test]
        public void Restore_WithNonPositiveCount_DropsTheDeadline()
        {
            var timer = new ReplenishmentTimer();
            timer.Restore(-5, T0.AddMinutes(5));

            Assert.AreEqual(0, timer.PendingCount);
            Assert.IsFalse(timer.NextDueUtc.HasValue);
        }
    }
}
