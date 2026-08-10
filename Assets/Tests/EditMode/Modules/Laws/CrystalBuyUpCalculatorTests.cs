using NUnit.Framework;
using KingdomRuler.Modules.Laws.Domain;

namespace KingdomRuler.Tests.EditMode.Modules.Laws
{
    /// <summary>
    /// Buy-up pricing is Laws-owned (GDD §6), so these live under Modules/Laws rather
    /// than Shared/Ledger — see the placement test in ARCHITECTURE.md §4.3.
    /// </summary>
    [TestFixture]
    public sealed class CrystalBuyUpCalculatorTests
    {
        [TestCase(100f, 20f, 5)]
        [TestCase(95f,  20f, 5)]
        [TestCase(101f, 20f, 6)]
        public void CalculateCost_RoundsUpToWholeCrystals(float remaining, float divisor, int expected)
        {
            Assert.AreEqual(expected, CrystalBuyUpCalculator.CalculateCost(remaining, divisor));
        }

        /// <summary>GDD §6: a tiny remainder still costs a crystal rather than rounding to free.</summary>
        [TestCase(1f,    20f, 1)]
        [TestCase(0.01f, 20f, 1)]
        public void CalculateCost_TinyButRealRemainder_CostsTheMinimumOfOne(
            float remaining, float divisor, int expected)
        {
            Assert.AreEqual(expected, CrystalBuyUpCalculator.CalculateCost(remaining, divisor));
        }

        /// <summary>
        /// Nothing left to buy costs nothing. The minimum-of-1 rule is about rounding a
        /// real remainder up, not about pricing a no-op — quoting 1 here contradicted
        /// LawsManager, which refuses that same purchase outright.
        /// </summary>
        [TestCase(0f,   20f)]
        [TestCase(-10f, 20f)]
        public void CalculateCost_NothingRemaining_IsFree(float remaining, float divisor)
        {
            Assert.AreEqual(0, CrystalBuyUpCalculator.CalculateCost(remaining, divisor));
        }

        [Test]
        public void CalculateCost_DivisorZero_ThrowsArgumentException()
        {
            Assert.Throws<System.ArgumentException>(() =>
                CrystalBuyUpCalculator.CalculateCost(100f, 0f));
        }
    }
}
