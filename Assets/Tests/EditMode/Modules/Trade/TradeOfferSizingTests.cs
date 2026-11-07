using System;
using System.Linq;
using NUnit.Framework;
using KingdomRuler.Modules.Trade;

namespace KingdomRuler.Tests.EditMode.Modules.Trade
{
    /// <summary>The offer size formula (GDD §7), checked against the worked numbers it was designed from.</summary>
    [TestFixture]
    public sealed class TradeOfferSizingTests
    {
        private const float Share = 0.20f;

        [Test]
        public void GiveTotal_AtBaseCapacity_Is40For2v2And60For3v3()
        {
            Assert.AreEqual(40, TradeOfferSizing.GiveTotal(Share, new[] { 100, 100 }, new[] { 100, 100 }));
            Assert.AreEqual(60, TradeOfferSizing.GiveTotal(Share, new[] { 100, 100, 100 }, new[] { 100, 100, 100 }));
            Assert.AreEqual(40, TradeOfferSizing.GiveTotal(Share, new[] { 100, 100, 100 }, new[] { 100, 100 }));
        }

        [Test]
        public void GiveTotal_UsesTheSmallerSide()
        {
            // Two level-5 warehouses into two level-0 ones stays small, in both directions.
            Assert.AreEqual(40, TradeOfferSizing.GiveTotal(Share, new[] { 760, 760 }, new[] { 100, 100 }));
            Assert.AreEqual(40, TradeOfferSizing.GiveTotal(Share, new[] { 100, 100 }, new[] { 760, 760 }));
            Assert.AreEqual(172, TradeOfferSizing.GiveTotal(Share, new[] { 760, 100 }, new[] { 760, 100 }));
        }

        [Test]
        public void GiveTotal_AtLevel10_Is2308()
        {
            Assert.AreEqual(2308, TradeOfferSizing.GiveTotal(Share, new[] { 5770, 5770 }, new[] { 5770, 5770 }));
        }

        [Test]
        public void GiveTotal_IsNeverBelowTheMinimum()
        {
            Assert.AreEqual(TradeOfferSizing.MinGiveTotal,
                TradeOfferSizing.GiveTotal(0.01f, new[] { 100, 100 }, new[] { 100, 100 }));
        }

        [Test]
        public void GiveTotal_RoundsHalvesAwayFromZero()
        {
            // 0.5 × 21 = 10.5 → 11; banker's rounding would give 10.
            Assert.AreEqual(11, TradeOfferSizing.GiveTotal(0.5f, new[] { 10, 11 }, new[] { 100, 100 }));
        }

        [Test]
        public void AtTheMinimum_TheBandsStayDistinct()
        {
            // From 10 up, 110% always rounds above the give total and 90% always below it, so a
            // "profitable" offer can never come out even.
            for (int give = TradeOfferSizing.MinGiveTotal; give <= 2000; give++)
            {
                Assert.Greater(TradeOfferSizing.ReceiveTotal(give, 1.10d), give, $"Profitable not above {give}.");
                Assert.Less(TradeOfferSizing.ReceiveTotal(give, 0.90d), give, $"Unprofitable not below {give}.");
            }
        }

        [Test]
        public void Split_SumsExactlyAndGivesEveryResourceAtLeastOne()
        {
            var rng = new Random(42);
            for (int i = 0; i < 2000; i++)
            {
                int count  = rng.Next(1, 4);
                var caps   = Enumerable.Range(0, count).Select(_ => rng.Next(1, 400_000)).ToArray();
                int total  = rng.Next(0, 5000);
                var parts  = TradeOfferSizing.Split(total, caps, rng);

                Assert.AreEqual(Math.Max(total, count), parts.Sum());
                Assert.That(parts, Is.All.GreaterThanOrEqualTo(1));
            }
        }

        [Test]
        public void Split_IsProportionalToCapacity()
        {
            var rng = new Random(7);
            for (int i = 0; i < 500; i++)
            {
                // Ten times the capacity gets 6–17 times the share (10 × 0.75/1.25 … 10 × 1.25/0.75).
                var lopsided = TradeOfferSizing.Split(1100, new[] { 1000, 100 }, rng);
                double ratio = lopsided[0] / (double)lopsided[1];
                Assert.That(ratio, Is.InRange(5.5d, 17.5d), $"Ratio {ratio}.");

                // Equal capacities get between 0.75/3.25 and 1.25/2.75 of the total each.
                var even = TradeOfferSizing.Split(1000, new[] { 100, 100, 100 }, rng);
                Assert.That(even, Is.All.InRange(225, 460));
            }
        }
    }
}
