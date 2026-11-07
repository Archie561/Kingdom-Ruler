using System;
using NUnit.Framework;
using KingdomRuler.Systems.Events;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Tests.EditMode.Systems.Ledger
{
    /// <summary>
    /// Whole-unit regeneration: one unit every <c>day ÷ capacity</c>, with the partial unit carried
    /// in the timestamp so nothing is ever stored as a fraction — or lost.
    /// </summary>
    [TestFixture]
    public sealed class TradeResourceRegenTests
    {
        private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private const TradeResourceType Wood = TradeResourceType.Wood;

        private static KingdomLedger LedgerWith(int capacity, int amount = 0)
        {
            var ledger = new KingdomLedger(new EventBus());
            ledger.SetWarehouseCapacity(Wood, capacity);
            if (amount > 0) ledger.AddTradeResource(Wood, amount);
            ledger.ResetRegenBaseline(Start);
            return ledger;
        }

        private static int AmountAt(KingdomLedger ledger, TimeSpan sinceStart)
        {
            ledger.AccruePassiveResourceRegen(Start + sinceStart);
            return ledger.GetTradeResource(Wood).Amount;
        }

        [Test]
        public void Regen_CreditsWholeUnitsOnly()
        {
            // Capacity 100: one unit every 864 seconds.
            var ledger = LedgerWith(100);

            Assert.AreEqual(0, AmountAt(ledger, TimeSpan.FromSeconds(863)));
            Assert.AreEqual(1, AmountAt(ledger, TimeSpan.FromSeconds(864)));
            Assert.AreEqual(1, AmountAt(ledger, TimeSpan.FromSeconds(1727)));
            Assert.AreEqual(2, AmountAt(ledger, TimeSpan.FromSeconds(1728)));
        }

        [TestCase(1)]
        [TestCase(7)]
        [TestCase(100)]
        [TestCase(333)]
        [TestCase(5770)]
        [TestCase(332_530)]
        [TestCase(KingdomLedger.MaxWarehouseCapacity)]
        public void Regen_EmptyWarehouseFillsInExactly24Hours_AtEveryCapacity(int capacity)
        {
            Assert.AreEqual(capacity - 1, AmountAt(LedgerWith(capacity), TimeSpan.FromDays(1) - TimeSpan.FromTicks(1)));
            Assert.AreEqual(capacity,     AmountAt(LedgerWith(capacity), TimeSpan.FromDays(1)));
            Assert.AreEqual(capacity,     AmountAt(LedgerWith(capacity), TimeSpan.FromDays(3)), "Never above capacity.");
        }

        [TestCase(7)]
        [TestCase(100)]
        [TestCase(5770)]
        public void Regen_ManySmallTicksEqualOneLongAbsence(int capacity)
        {
            // The game settles at 4 Hz while open and once on resume; both must agree.
            var ticked = LedgerWith(capacity);
            var once   = LedgerWith(capacity);
            var tick   = TimeSpan.FromMilliseconds(250);
            var span   = TimeSpan.FromHours(10) + TimeSpan.FromMilliseconds(130);

            for (var t = TimeSpan.Zero; t <= span; t += tick) ticked.AccruePassiveResourceRegen(Start + t);
            once.AccruePassiveResourceRegen(Start + span);
            ticked.AccruePassiveResourceRegen(Start + span);

            Assert.AreEqual(once.GetTradeResource(Wood).Amount, ticked.GetTradeResource(Wood).Amount);
        }

        [Test]
        public void Regen_FullWarehouseBanksNothing()
        {
            // A full warehouse that sat for ten hours must not burst-refill the moment it is spent from.
            var ledger = LedgerWith(100, 100);
            ledger.AccruePassiveResourceRegen(Start.AddHours(10));

            ledger.SpendTradeResource(Wood, 10);

            Assert.AreEqual(90, AmountAt(ledger, TimeSpan.FromHours(10) + TimeSpan.FromSeconds(1)));
        }

        [Test]
        public void Regen_AfterAnUpgrade_KeepsTheProgressMade()
        {
            // Half a unit at capacity 100 (432 s of 864), then double the capacity: units now take
            // 432 s, so 216 s more completes one. Resetting the baseline would give none.
            var ledger = LedgerWith(100);
            ledger.AccruePassiveResourceRegen(Start.AddSeconds(432));
            ledger.SetWarehouseCapacity(Wood, 200);

            Assert.AreEqual(1, AmountAt(ledger, TimeSpan.FromSeconds(648)));
        }

        [Test]
        public void NextUnitProgress_RisesFromZeroToOneAcrossOneUnit()
        {
            var ledger = LedgerWith(100);
            var wood   = ledger.GetTradeResource(Wood);

            Assert.AreEqual(0f,    wood.NextUnitProgress(Start), 1e-6f);
            Assert.AreEqual(0.25f, wood.NextUnitProgress(Start.AddSeconds(216)), 1e-6f);
            Assert.AreEqual(0.5f,  wood.NextUnitProgress(Start.AddSeconds(432)), 1e-6f);

            ledger.AccruePassiveResourceRegen(Start.AddSeconds(864));
            Assert.AreEqual(1, wood.Amount);
            Assert.AreEqual(0f, wood.NextUnitProgress(Start.AddSeconds(864)), 1e-6f, "A new unit starts from nothing.");
        }

        [Test]
        public void NextUnitProgress_IsZeroWhenFull()
        {
            var ledger = LedgerWith(100, 100);

            Assert.AreEqual(0f, ledger.GetTradeResource(Wood).NextUnitProgress(Start.AddHours(1)));
        }

        [Test]
        public void TimeUntilFull_IsExactAndZeroWhenFull()
        {
            var ledger = LedgerWith(100, 90);
            var wood   = ledger.GetTradeResource(Wood);

            Assert.AreEqual(TimeSpan.FromSeconds(8640), wood.TimeUntilFull(Start), "Ten units × 864 s.");
            Assert.AreEqual(TimeSpan.FromSeconds(8540), wood.TimeUntilFull(Start.AddSeconds(100)));

            ledger.AccruePassiveResourceRegen(Start.AddSeconds(8640));
            Assert.AreEqual(100, wood.Amount);
            Assert.AreEqual(TimeSpan.Zero, wood.TimeUntilFull(Start.AddSeconds(8640)));
        }

        [Test]
        public void ABackwardsDeviceClock_DoesNotStallRegenForever()
        {
            // A clock that ran a year ahead and was corrected: the baseline resyncs rather than
            // waiting out the year.
            var ledger = LedgerWith(100);
            ledger.ResetRegenBaseline(Start.AddYears(1));

            ledger.AccruePassiveResourceRegen(Start);
            Assert.AreEqual(1, AmountAt(ledger, TimeSpan.FromSeconds(864)));
        }
    }
}
