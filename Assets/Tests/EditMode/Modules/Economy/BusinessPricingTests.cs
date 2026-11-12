using NUnit.Framework;
using KingdomRuler.Modules.Economy;

namespace KingdomRuler.Tests.EditMode.Modules.Economy
{
    /// <summary>Flat prices, "Max", and the crystal top-up's ceil rule (GDD §8) — without overflow anywhere.</summary>
    [TestFixture]
    public sealed class BusinessPricingTests
    {
        [TestCase(100L, 1, 100L)]
        [TestCase(100L, 10, 1_000L)]
        [TestCase(40_000_000L, 1_000_000, 40_000_000_000_000L)]
        public void PriceOf_IsFlat_UnitPriceTimesCount(long unitPrice, int count, long expected)
        {
            Assert.AreEqual(expected, BusinessPricing.PriceOf(unitPrice, count));
        }

        [Test]
        public void PriceOf_NothingCostsNothing_AndAnAbsurdPriceSaturates()
        {
            Assert.AreEqual(0, BusinessPricing.PriceOf(100, 0));
            Assert.AreEqual(0, BusinessPricing.PriceOf(100, -5));
            Assert.AreEqual(long.MaxValue, BusinessPricing.PriceOf(long.MaxValue / 2, 3));
        }

        [TestCase(1_050L, 100L, 1_000_000, 10)]
        [TestCase(99L,    100L, 1_000_000, 0)]
        [TestCase(100L,   100L, 1_000_000, 1)]
        [TestCase(10_000L, 100L, 7,        7)]
        [TestCase(10_000L, 100L, 0,        0)]
        public void AffordableCount_IsWhatTheGoldPaysFor_WithinTheRoomLeft(long gold, long unitPrice, int room, int expected)
        {
            Assert.AreEqual(expected, BusinessPricing.AffordableCount(gold, unitPrice, room));
        }

        [TestCase(360L, 4L)]    // GDD §8 / plan §5: ×10 bakeries (1,000) holding 640
        [TestCase(60L,  1L)]    // Max holding 40 offers one bakery
        [TestCase(200L, 2L)]    // an exact multiple rounds nowhere
        [TestCase(1L,   1L)]    // at least one crystal
        [TestCase(101L, 2L)]
        public void CrystalsToCover_IsCeilOfTheShortfall_AtLeastOne(long missing, long expected)
        {
            Assert.AreEqual(expected, BusinessPricing.CrystalsToCover(missing, 100));
        }

        [Test]
        public void CrystalsToCover_NothingMissing_IsZero_AndAHugeShortfallDoesNotOverflow()
        {
            Assert.AreEqual(0, BusinessPricing.CrystalsToCover(0, 100));
            Assert.AreEqual(0, BusinessPricing.CrystalsToCover(-50, 100));
            Assert.AreEqual(long.MaxValue / 100 + 1, BusinessPricing.CrystalsToCover(long.MaxValue, 100));
        }

        [Test]
        public void CrystalsToCover_ANonPositiveRate_CountsOneGoldPerCrystal()
        {
            Assert.AreEqual(250, BusinessPricing.CrystalsToCover(250, 0));
        }
    }
}
