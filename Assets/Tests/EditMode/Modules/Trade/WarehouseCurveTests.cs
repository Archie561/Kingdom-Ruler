using System;
using NUnit.Framework;
using KingdomRuler.Modules.Trade.Domain;

namespace KingdomRuler.Tests.EditMode.Modules.Trade
{
    /// <summary>
    /// The warehouse growth formula that replaced the config's lookup arrays.
    /// </summary>
    [TestFixture]
    public sealed class WarehouseCurveTests
    {
        private static WarehouseCurve Capacity => WarehouseCurve.DefaultCapacity;

        [Test]
        public void ValueAt_Level0_IsTheBase()
        {
            Assert.AreEqual(100f, Capacity.ValueAt(0), 0.001f);
        }

        [Test]
        public void ValueAt_UsesBankersRounding_MatchingLevelingCurve()
        {
            // 100 × 1.5² = 225 → 22.5 → Math.Round's ToEven gives 22 → 220, NOT 230.
            // Deliberately inherited so this curve and LevelingCurve share one expression;
            // matching the sibling matters more than the one-unit difference. Do not "fix".
            Assert.AreEqual(220f, Capacity.ValueAt(2), 0.001f);
        }

        [Test]
        public void ValueAt_IsStrictlyIncreasing()
        {
            // The property the old arrays broke past their last entry: capacity kept growing
            // exponentially while the crystal price fell back to a linear formula.
            float previous = Capacity.ValueAt(0);
            for (int level = 1; level <= 40; level++)
            {
                float current = Capacity.ValueAt(level);
                Assert.Greater(current, previous, $"Capacity did not grow at level {level}.");
                previous = current;
            }
        }

        [Test]
        public void ValueAt_IsFiniteAtAnAbsurdLevel()
        {
            // Clamped rather than allowed to overflow to Infinity, which would give an infinite
            // regen rate and NaN amounts — an unrecoverable save (CLAUDE.md §1.2).
            float value = Capacity.ValueAt(100_000);

            Assert.IsFalse(float.IsInfinity(value));
            Assert.IsFalse(float.IsNaN(value));
            Assert.AreEqual(Capacity.ValueAt(WarehouseCurve.MaxSupportedLevel), value, 0.001f);
        }

        [Test]
        public void ValueAt_NegativeLevel_Throws()
        {
            Assert.Throws<ArgumentException>(() => Capacity.ValueAt(-1));
        }

        [Test]
        public void DefaultStruct_IsInvalidButStillProducesASaneValue()
        {
            // default(WarehouseCurve) bypasses the constructor and has all-zero coefficients.
            // Returning 0 would mean a zero-capacity warehouse and a zero regen rate, stranding
            // the resource empty forever — so every field falls back individually.
            var zeroed = default(WarehouseCurve);

            Assert.IsFalse(zeroed.IsValid);
            Assert.AreEqual(WarehouseCurve.DefaultBaseValue, zeroed.ValueAt(0), 0.001f);
            Assert.Greater(zeroed.ValueAt(3), 0f);
        }

        [Test]
        public void IsValid_RejectsNonPositiveCoefficients()
        {
            Assert.IsFalse(new WarehouseCurve(0f, 1.5f, 10f).IsValid);
            Assert.IsFalse(new WarehouseCurve(100f, 0f, 10f).IsValid);
            Assert.IsFalse(new WarehouseCurve(100f, 1.5f, 0f).IsValid);
            Assert.IsTrue(new WarehouseCurve(100f, 1.5f, 10f).IsValid);
        }

        [Test]
        public void ValueAt_NeverReturnsZero_EvenForAShrinkingCurve()
        {
            // A growth below 1 rounds toward zero at high levels; a zero capacity would brick
            // the warehouse, so the result is floored at roundTo.
            var shrinking = new WarehouseCurve(100f, 0.5f, 10f);

            for (int level = 0; level <= 30; level++)
                Assert.Greater(shrinking.ValueAt(level), 0f, $"Zero at level {level}.");
        }

        [Test]
        public void CrystalCostCurve_MatchesTheOldHandAuthoredTable_AtTheFirstLevels()
        {
            // The array this replaced was {5, 8, 12, 18, 25}. The formula reproduces its shape,
            // so the change is a generalisation rather than a retune.
            var cost = WarehouseCurve.DefaultCrystalCost;

            Assert.AreEqual(5f,  cost.ValueAt(0), 0.001f);
            Assert.AreEqual(8f,  cost.ValueAt(1), 0.001f);   // 7.5 → 8
            Assert.AreEqual(25f, cost.ValueAt(4), 0.001f);   // 25.3 → 25
        }
    }
}
