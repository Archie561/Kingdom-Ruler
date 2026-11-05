using System;
using NUnit.Framework;
using KingdomRuler.Systems.Events;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Tests.EditMode.Systems.Ledger
{
    [TestFixture]
    public sealed class CharacteristicLevelingCurveTests
    {
        // GDD §6: required(N) = round(100 × 1.35^(N-1) / 10) × 10
        [TestCase(1, 100)]
        [TestCase(2, 140)]
        [TestCase(3, 180)]
        [TestCase(4, 250)]
        [TestCase(5, 330)]
        public void PointsRequired_MatchesGddFormula(int level, int expected)
        {
            Assert.AreEqual(expected, CharacteristicLevelingCurve.PointsRequired(level));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void PointsRequired_Level0OrNegative_ThrowsArgumentException(int level)
        {
            Assert.Throws<ArgumentException>(() => CharacteristicLevelingCurve.PointsRequired(level));
        }

        /// <summary>
        /// The Ledger's level-up loop runs while accumulated points meet the requirement, so a
        /// zero requirement would spin forever and publish level-up events without end. With
        /// fixed constants that cannot happen — this fails the moment an edit to them would let it.
        /// </summary>
        /// <summary>Far beyond play the formula would overflow an int; it is capped instead, and stays positive.</summary>
        [Test]
        public void PointsRequired_IsCapped_AtExtremeLevels()
        {
            Assert.AreEqual(CharacteristicLevelingCurve.MaxPointsRequired, CharacteristicLevelingCurve.PointsRequired(500));
        }

        [Test]
        public void PointsRequired_IsPositiveAndRising_ForEveryReachableLevel()
        {
            int previous = 0;
            for (int level = 1; level <= 200; level++)
            {
                int current = CharacteristicLevelingCurve.PointsRequired(level);
                Assert.Greater(current, 0, $"Non-positive requirement at level {level}.");
                Assert.GreaterOrEqual(current, previous, $"The requirement fell at level {level}.");
                previous = current;
            }
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
            var viaLaws        = new KingdomLedger(new EventBus());
            var viaOccurrences = new KingdomLedger(new EventBus());

            const int points = 375;
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
            var atOnce   = new KingdomLedger(new EventBus());
            var inPieces = new KingdomLedger(new EventBus());

            atOnce.AddCharacteristicPoints(CharacteristicType.Science, 300);
            foreach (var chunk in new[] { 120, 90, 90 })
                inPieces.AddCharacteristicPoints(CharacteristicType.Science, chunk);

            var a = atOnce.GetCharacteristic(CharacteristicType.Science);
            var b = inPieces.GetCharacteristic(CharacteristicType.Science);

            Assert.AreEqual(a.Level, b.Level);
            Assert.AreEqual(a.PointsIntoCurrentLevel, b.PointsIntoCurrentLevel);
        }
    }
}
