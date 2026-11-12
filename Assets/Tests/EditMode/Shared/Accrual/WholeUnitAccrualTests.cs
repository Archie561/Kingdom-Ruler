using System;
using System.Numerics;
using NUnit.Framework;
using KingdomRuler.Shared.Accrual;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Tests.EditMode.Shared.Accrual
{
    /// <summary>
    /// The shared whole-unit fill: one unit every <c>period ÷ capacity</c>, the partial unit carried
    /// in the start timestamp, exact from a capacity of 1 to the top of <c>long</c>. Warehouse regen
    /// runs through it too — <c>TradeResourceRegenTests</c> pins that behaviour unchanged.
    /// </summary>
    [TestFixture]
    public sealed class WholeUnitAccrualTests
    {
        private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly TimeSpan Day   = TimeSpan.FromDays(1);

        /// <summary>The Ledger's gold ceiling: the largest storage Economy can have.</summary>
        private const long GoldCeiling = KingdomLedger.MaxGold;

        /// <summary>A store, settled the way the game settles it.</summary>
        private sealed class Store
        {
            public long     Amount;
            public long     Capacity;
            public DateTime StartedUtc = Start;
            public TimeSpan Period     = Day;

            public long SettleAt(DateTime now)
            {
                Amount = WholeUnitAccrual.Settle(Amount, Capacity, StartedUtc, Period, now, out StartedUtc);
                return Amount;
            }
        }

        private static Store Empty(long capacity, TimeSpan? period = null) =>
            new() { Capacity = capacity, Period = period ?? Day };

        // ── Filling ───────────────────────────────────────────────────────────────

        [Test]
        public void Settle_CreditsWholeUnitsOnly()
        {
            // Capacity 100 over a day: one unit every 864 seconds.
            var store = Empty(100);

            Assert.AreEqual(0, store.SettleAt(Start.AddSeconds(863)));
            Assert.AreEqual(1, store.SettleAt(Start.AddSeconds(864)));
            Assert.AreEqual(1, store.SettleAt(Start.AddSeconds(1727)));
            Assert.AreEqual(2, store.SettleAt(Start.AddSeconds(1728)));
        }

        [TestCase(1L)]
        [TestCase(7L)]
        [TestCase(100L)]
        [TestCase(10_000_000L)]
        [TestCase(1_000_000_000_000L)]
        [TestCase(GoldCeiling)]
        [TestCase(long.MaxValue)]
        public void Settle_EmptyStoreFillsInExactlyOnePeriod_AtEveryCapacity(long capacity)
        {
            long justShort = Empty(capacity).SettleAt(Start + Day - TimeSpan.FromTicks(1));

            Assert.Less(justShort, capacity, "One tick early it is not yet full.");
            Assert.AreEqual(ExpectedUnits(Day.Ticks - 1, capacity, Day.Ticks), justShort);
            Assert.AreEqual(capacity, Empty(capacity).SettleAt(Start + Day));
            Assert.AreEqual(capacity, Empty(capacity).SettleAt(Start + TimeSpan.FromDays(3)), "Never above capacity.");
        }

        [TestCase(1)]
        [TestCase(7)]
        [TestCase(24)]
        public void Settle_FillsInExactlyOnePeriod_WhateverThePeriod(int hours)
        {
            var period = TimeSpan.FromHours(hours);

            Assert.AreEqual(1439, Empty(1440, period).SettleAt(Start + period - TimeSpan.FromTicks(1)));
            Assert.AreEqual(1440, Empty(1440, period).SettleAt(Start + period));
        }

        [TestCase(7L)]
        [TestCase(100L)]
        [TestCase(5770L)]
        public void Settle_ManySmallTicksEqualOneLongAbsence(long capacity)
        {
            // The game settles at 4 Hz while open and once on resume; both must agree.
            var (ticked, once) = TickedAndOnce(capacity, TimeSpan.FromHours(10) + TimeSpan.FromMilliseconds(130), out _);

            Assert.AreEqual(once, ticked);
        }

        [TestCase(10_000_000_000L)]
        [TestCase(GoldCeiling)]
        public void Settle_AtGoldScale_ManyTicksRunAheadOnlyBySlivers(long capacity)
        {
            // When a unit takes about a tick or less, the timestamp cannot hold the partial unit
            // exactly: each settle may recount under one tick, in the player's favour, never against.
            var span = TimeSpan.FromHours(10) + TimeSpan.FromMilliseconds(130);
            var (ticked, once) = TickedAndOnce(capacity, span, out int settles);

            long perSettle = capacity / Day.Ticks + 1;   // ⌈capacity ÷ period ticks⌉
            Assert.GreaterOrEqual(ticked, once, "Never behind one long absence.");
            Assert.LessOrEqual(ticked - once, settles * perSettle, "Ahead by under one tick's production per settle.");
            Assert.Less((ticked - once) / (double)once, 1e-6, "A rounding sliver, not a rate change.");
        }

        [Test]
        public void Settle_AtHugeCapacities_MatchesExactArithmetic()
        {
            // Products far outside long: checked against BigInteger, an independent oracle.
            var rng = new Random(20261112);
            for (int i = 0; i < 2000; i++)
            {
                long capacity = NextLong(rng, 10_000_000L, long.MaxValue);
                long amount   = NextLong(rng, 0L, capacity);
                long elapsed  = NextLong(rng, 1L, Day.Ticks - 1);

                long settled = WholeUnitAccrual.Settle(amount, capacity, Start, Day, Start.AddTicks(elapsed), out var startedAfter);

                long units    = ExpectedUnits(elapsed, capacity, Day.Ticks);
                long expected = (long)BigInteger.Min((BigInteger)amount + units, capacity);
                Assert.AreEqual(expected, settled, $"capacity {capacity}, amount {amount}, elapsed {elapsed}");

                var expectedStart = expected >= capacity
                    ? Start.AddTicks(elapsed)
                    : Start.AddTicks((long)((BigInteger)units * Day.Ticks / capacity));
                Assert.AreEqual(expectedStart, startedAfter, $"capacity {capacity}, amount {amount}, elapsed {elapsed}");
            }
        }

        [Test]
        public void Settle_AfterACapacityChange_KeepsTheProgressMade()
        {
            // Half a unit at capacity 100 (432 s of 864), then double the capacity: units now take
            // 432 s, so the time already spent completes one. Resetting the start would give none.
            var store = Empty(100);
            store.SettleAt(Start.AddSeconds(432));
            store.Capacity = 200;

            Assert.AreEqual(1, store.SettleAt(Start.AddSeconds(432)));
            Assert.AreEqual(1, store.SettleAt(Start.AddSeconds(647)));
            Assert.AreEqual(2, store.SettleAt(Start.AddSeconds(864)));
        }

        [Test]
        public void Settle_FullStoreBanksNothing()
        {
            // Full for ten hours, then emptied a little: it must not burst-refill.
            var store = new Store { Amount = 100, Capacity = 100 };
            store.SettleAt(Start.AddHours(10));
            store.Amount -= 10;

            Assert.AreEqual(90, store.SettleAt(Start.AddHours(10) + TimeSpan.FromSeconds(1)));
            Assert.AreEqual(91, store.SettleAt(Start.AddHours(10) + TimeSpan.FromSeconds(864)));
        }

        [Test]
        public void Settle_AFutureStart_ResynchronisesInsteadOfStalling()
        {
            // A clock that ran a year ahead and was corrected.
            var store = new Store { Capacity = 100, StartedUtc = Start.AddYears(1) };

            Assert.AreEqual(0, store.SettleAt(Start));
            Assert.AreEqual(Start, store.StartedUtc);
            Assert.AreEqual(1, store.SettleAt(Start.AddSeconds(864)));
        }

        [TestCase(0L)]
        [TestCase(-5L)]
        public void ZeroCapacity_ProducesNothing_AndCountsAsFull(long capacity)
        {
            var store = Empty(capacity);

            Assert.AreEqual(0, store.SettleAt(Start + TimeSpan.FromDays(2)));
            Assert.AreEqual(Start + TimeSpan.FromDays(2), store.StartedUtc, "Its start follows the clock, like a full store's.");
            Assert.AreEqual(0f, WholeUnitAccrual.NextUnitProgress(0, capacity, Start, Day, Start.AddHours(1)));
            Assert.AreEqual(TimeSpan.Zero, WholeUnitAccrual.TimeUntilFull(0, capacity, Start, Day, Start.AddHours(1)));
            Assert.AreEqual(TimeSpan.MaxValue, WholeUnitAccrual.TimePerUnit(capacity, Day));
        }

        [Test]
        public void ANonPositivePeriod_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => WholeUnitAccrual.Settle(0, 10, Start, TimeSpan.Zero, Start, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => WholeUnitAccrual.TimePerUnit(10, TimeSpan.FromTicks(-1)));
        }

        // ── Read-outs ─────────────────────────────────────────────────────────────

        [Test]
        public void NextUnitProgress_RisesFromZeroToOneAcrossOneUnit()
        {
            Assert.AreEqual(0f,    WholeUnitAccrual.NextUnitProgress(0, 100, Start, Day, Start), 1e-6f);
            Assert.AreEqual(0.25f, WholeUnitAccrual.NextUnitProgress(0, 100, Start, Day, Start.AddSeconds(216)), 1e-6f);
            Assert.AreEqual(0.5f,  WholeUnitAccrual.NextUnitProgress(0, 100, Start, Day, Start.AddSeconds(432)), 1e-6f);
            Assert.AreEqual(0f,    WholeUnitAccrual.NextUnitProgress(100, 100, Start, Day, Start.AddHours(1)), "Zero while full.");
        }

        [Test]
        public void NextUnitProgress_AtTheCeiling_IsExact()
        {
            // An hour and a few ticks at the ceiling: elapsed × capacity is ~3.6 × 10²⁵, far outside long.
            const long elapsed = 36_000_000_007;
            float progress = WholeUnitAccrual.NextUnitProgress(0, GoldCeiling, Start, Day, Start.AddTicks(elapsed));

            double exact = (double)((BigInteger)elapsed * GoldCeiling % Day.Ticks) / Day.Ticks;
            Assert.AreEqual((float)exact, progress, 1e-6f);
        }

        [Test]
        public void TimeUntilFull_IsExactAndZeroWhenFull()
        {
            Assert.AreEqual(TimeSpan.FromSeconds(8640), WholeUnitAccrual.TimeUntilFull(90, 100, Start, Day, Start), "Ten units × 864 s.");
            Assert.AreEqual(TimeSpan.FromSeconds(8540), WholeUnitAccrual.TimeUntilFull(90, 100, Start, Day, Start.AddSeconds(100)));
            Assert.AreEqual(TimeSpan.Zero,              WholeUnitAccrual.TimeUntilFull(100, 100, Start, Day, Start));
        }

        [TestCase(GoldCeiling)]
        [TestCase(long.MaxValue)]
        public void TimeUntilFull_AtTheCeiling_IsOnePeriodFromEmpty(long capacity)
        {
            Assert.AreEqual(Day, WholeUnitAccrual.TimeUntilFull(0, capacity, Start, Day, Start));
            Assert.AreEqual(Day - TimeSpan.FromHours(6),
                            WholeUnitAccrual.TimeUntilFull(0, capacity, Start, Day, Start.AddHours(6)));
        }

        [Test]
        public void TimePerUnit_IsThePeriodOverTheCapacity()
        {
            Assert.AreEqual(TimeSpan.FromSeconds(864), WholeUnitAccrual.TimePerUnit(100, Day));
            Assert.AreEqual(TimeSpan.FromSeconds(60),  WholeUnitAccrual.TimePerUnit(1440, Day));
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>The amount after settling every 250 ms across <paramref name="span"/>, and after settling once at its end.</summary>
        private static (long ticked, long once) TickedAndOnce(long capacity, TimeSpan span, out int settles)
        {
            var ticked = Empty(capacity);
            var once   = Empty(capacity);
            var tick   = TimeSpan.FromMilliseconds(250);

            settles = 0;
            for (var t = TimeSpan.Zero; t <= span; t += tick, settles++) ticked.SettleAt(Start + t);
            ticked.SettleAt(Start + span);
            settles++;

            return (ticked.Amount, once.SettleAt(Start + span));
        }

        /// <summary>⌊elapsed × capacity ÷ period⌋, exactly.</summary>
        private static long ExpectedUnits(long elapsed, long capacity, long period) =>
            (long)((BigInteger)elapsed * capacity / period);

        /// <summary>A uniform long in [min, max]: two doubles do not carry 63 bits, so it is built from bytes.</summary>
        private static long NextLong(Random rng, long min, long max)
        {
            var bytes = new byte[8];
            rng.NextBytes(bytes);
            ulong range = (ulong)(max - min) + 1UL;
            ulong value = BitConverter.ToUInt64(bytes, 0);
            return min + (long)(range == 0 ? value : value % range);
        }
    }
}
