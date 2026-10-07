using System;
using NUnit.Framework;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Tests.EditMode.Systems.Ledger
{
    [TestFixture]
    public sealed class LevelingCurveTests
    {
        // GDD §6: required(N) = round(100 × 1.35^(N-1) / 10) × 10
        [TestCase(1, 100f)]
        [TestCase(2, 140f)]
        [TestCase(3, 180f)]
        [TestCase(4, 250f)]
        [TestCase(5, 330f)]
        public void PointsRequired_DefaultCurve_MatchesGddFormula(int level, float expected)
        {
            Assert.AreEqual(expected, LevelingCurve.Default.PointsRequired(level));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void PointsRequired_Level0OrNegative_ThrowsArgumentException(int level)
        {
            Assert.Throws<ArgumentException>(() => LevelingCurve.Default.PointsRequired(level));
        }

        [Test]
        public void PointsRequired_RespectsCustomCoefficients()
        {
            var curve = new LevelingCurve(basePoints: 50f, growthFactor: 2f, roundToNearest: 5f);
            Assert.AreEqual(50f,  curve.PointsRequired(1));
            Assert.AreEqual(100f, curve.PointsRequired(2));
            Assert.AreEqual(200f, curve.PointsRequired(3));
        }

        /// <summary>
        /// The Ledger's level-up loop runs while accumulated points meet the requirement,
        /// so a zero or negative requirement would spin forever and publish level-up events
        /// without end. The curve must never hand back such a value, whatever it was built from.
        /// </summary>
        [Test]
        public void PointsRequired_IsAlwaysPositive_EvenForADefaultConstructedCurve()
        {
            var zeroed = default(LevelingCurve);
            for (int level = 1; level <= 50; level++)
                Assert.Greater(zeroed.PointsRequired(level), 0f,
                    $"default(LevelingCurve) returned a non-positive requirement at level {level}.");
        }

        [Test]
        public void PointsRequired_IsAlwaysPositive_ForAShrinkingCurve()
        {
            // A growth factor below 1 shrinks toward zero; it must still floor above zero.
            var shrinking = new LevelingCurve(basePoints: 100f, growthFactor: 0.5f, roundToNearest: 10f);
            for (int level = 1; level <= 50; level++)
                Assert.Greater(shrinking.PointsRequired(level), 0f,
                    $"Shrinking curve returned a non-positive requirement at level {level}.");
        }

        [Test]
        public void IsValid_IsFalseForZeroedOrNonPositiveCoefficients()
        {
            Assert.IsFalse(default(LevelingCurve).IsValid);
            Assert.IsFalse(new LevelingCurve(0f, 1.35f, 10f).IsValid);
            Assert.IsFalse(new LevelingCurve(100f, 0f, 10f).IsValid);
            Assert.IsFalse(new LevelingCurve(100f, 1.35f, 0f).IsValid);
            Assert.IsTrue(LevelingCurve.Default.IsValid);
        }

        [Test]
        public void KingdomLedger_RejectsAnInvalidCurveAtConstruction()
        {
            Assert.Throws<ArgumentException>(() =>
                new KingdomLedger(new KingdomRuler.Systems.Events.EventBus(), default(LevelingCurve)));
        }
    }

    /// <summary>
    /// The reason the curve is Ledger-owned: Laws and Random Occurrences both award
    /// characteristic points, and they used to pass their own points-required delegate.
    /// Laws built one from its config while the occurrences module passed a flat stub, so
    /// the same characteristic levelled at different rates depending on which mechanic
    /// touched it. No API on the Ledger should let a caller reintroduce that.
    /// </summary>
    [TestFixture]
    public sealed class LevelingRateParityTests
    {
        [Test]
        public void SamePointsFromAnySource_ProduceTheSameLevelAndProgress()
        {
            var busA = new KingdomRuler.Systems.Events.EventBus();
            var busB = new KingdomRuler.Systems.Events.EventBus();
            var viaLaws        = new KingdomLedger(busA);
            var viaOccurrences = new KingdomLedger(busB);

            const float points = 375f;
            viaLaws.AddCharacteristicPoints(CharacteristicType.Army, points);
            viaOccurrences.AddCharacteristicPoints(CharacteristicType.Army, points);

            var a = viaLaws.GetCharacteristic(CharacteristicType.Army);
            var b = viaOccurrences.GetCharacteristic(CharacteristicType.Army);

            Assert.AreEqual(a.Level, b.Level, "Same points must yield the same level.");
            Assert.AreEqual(a.PointsIntoCurrentLevel, b.PointsIntoCurrentLevel,
                "Same points must yield the same progress into the level.");
        }

        [Test]
        public void PointsAppliedInPieces_MatchTheSameTotalAppliedAtOnce()
        {
            var atOnce    = new KingdomLedger(new KingdomRuler.Systems.Events.EventBus());
            var inPieces  = new KingdomLedger(new KingdomRuler.Systems.Events.EventBus());

            atOnce.AddCharacteristicPoints(CharacteristicType.Science, 300f);
            foreach (var chunk in new[] { 120f, 90f, 90f })
                inPieces.AddCharacteristicPoints(CharacteristicType.Science, chunk);

            var a = atOnce.GetCharacteristic(CharacteristicType.Science);
            var b = inPieces.GetCharacteristic(CharacteristicType.Science);

            Assert.AreEqual(a.Level, b.Level);
            Assert.AreEqual(a.PointsIntoCurrentLevel, b.PointsIntoCurrentLevel, 0.001f);
        }
    }

}
