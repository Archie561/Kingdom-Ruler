using NUnit.Framework;
using UnityEngine;
using KingdomRuler.Modules.Trade;

namespace KingdomRuler.Tests.EditMode.Modules.Trade
{
    /// <summary>
    /// The two warehouse growth formulas on <see cref="TradeConfig"/> — capacity at a level, and
    /// the crystal price of the next upgrade — which replaced the config's lookup arrays.
    /// </summary>
    [TestFixture]
    public sealed class WarehouseUpgradeCurveTests
    {
        private TradeConfig _config;

        [SetUp]
        public void SetUp() => _config = ScriptableObject.CreateInstance<TradeConfig>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_config);

        // ── Capacity ──────────────────────────────────────────────────────────────

        [Test]
        public void Capacity_AtLevel0_IsTheBase()
        {
            Assert.AreEqual(100f, _config.WarehouseCapacityAt(0), 0.001f);
        }

        [Test]
        public void Capacity_UsesBankersRounding_MatchingTheCharacteristicLevelingCurve()
        {
            // 100 × 1.5² = 225 → 22.5 → Math.Round's ToEven gives 22 → 220, NOT 230.
            // Deliberately inherited so both curves share one expression; matching the sibling
            // matters more than the one-unit difference. Do not "fix".
            Assert.AreEqual(220f, _config.WarehouseCapacityAt(2), 0.001f);
        }

        [Test]
        public void Capacity_IsStrictlyIncreasing()
        {
            // The property the old arrays broke past their last entry: capacity kept growing
            // exponentially while the crystal price fell back to a linear formula.
            float previous = _config.WarehouseCapacityAt(0);
            for (int level = 1; level <= 40; level++)
            {
                float current = _config.WarehouseCapacityAt(level);
                Assert.Greater(current, previous, $"Capacity did not grow at level {level}.");
                previous = current;
            }
        }

        [Test]
        public void Capacity_IsFiniteAtAnAbsurdLevel()
        {
            // Clamped rather than allowed to overflow to Infinity, which would give an infinite
            // regen rate and NaN amounts — an unrecoverable save (CLAUDE.md §1.2).
            float value = _config.WarehouseCapacityAt(100_000);

            Assert.IsFalse(float.IsInfinity(value));
            Assert.IsFalse(float.IsNaN(value));
            Assert.AreEqual(_config.WarehouseCapacityAt(TradeConfig.MaxWarehouseLevel), value, 0.001f);
        }

        [Test]
        public void Capacity_ANegativeLevel_IsTreatedAsLevel0()
        {
            // A corrupt save degrades to an unupgraded warehouse rather than refusing to load.
            Assert.AreEqual(_config.WarehouseCapacityAt(0), _config.WarehouseCapacityAt(-5), 0.001f);
        }

        [Test]
        public void Capacity_NeverReturnsZero_WhateverTheAssetHolds()
        {
            // A zero capacity would mean a zero regen rate, stranding the warehouse empty forever.
            // OnValidate stops a designer typing these, but a hand-edited asset bypasses it.
            _config.CapacityBase    = 0f;
            _config.CapacityGrowth  = 0.5f;   // shrinking, so it rounds toward zero
            _config.CapacityRoundTo = 0f;

            for (int level = 0; level <= 30; level++)
                Assert.Greater(_config.WarehouseCapacityAt(level), 0f, $"Zero at level {level}.");
        }

        // ── Crystal price ─────────────────────────────────────────────────────────

        [Test]
        public void CrystalCost_MatchesTheOldHandAuthoredTable_AtTheFirstLevels()
        {
            // The array this replaced was {5, 8, 12, 18, 25}. The formula reproduces its shape,
            // so the change is a generalisation rather than a retune.
            Assert.AreEqual(5,  _config.WarehouseUpgradeCrystalCost(0));
            Assert.AreEqual(8,  _config.WarehouseUpgradeCrystalCost(1));   // 7.5 → 8
            Assert.AreEqual(25, _config.WarehouseUpgradeCrystalCost(4));   // 25.3 → 25
        }

        [Test]
        public void CrystalCost_IsStrictlyIncreasing()
        {
            // The property the old array's `5 + 3*level` fallback broke: past the table, cost grew
            // linearly while capacity grew exponentially, so a level-50 warehouse was cheap.
            int previous = _config.WarehouseUpgradeCrystalCost(0);
            for (int level = 1; level <= 40; level++)
            {
                int current = _config.WarehouseUpgradeCrystalCost(level);
                Assert.Greater(current, previous, $"Cost did not rise at level {level}.");
                previous = current;
            }
        }

        [Test]
        public void CrystalCost_IsAlwaysAtLeastOne()
        {
            // A free upgrade would be an infinite capacity loop.
            _config.CrystalCostBase   = 0.001f;
            _config.CrystalCostGrowth = 1.01f;

            for (int level = 0; level <= 20; level++)
                Assert.GreaterOrEqual(_config.WarehouseUpgradeCrystalCost(level), 1);
        }

        [Test]
        public void CrystalCost_StaysPositiveAtAnAbsurdLevel()
        {
            // 5 × 1.5^50 exceeds int.MaxValue; wrapping to a negative price would make the
            // upgrade free or pay the player.
            Assert.Greater(_config.WarehouseUpgradeCrystalCost(200), 0);
        }
    }
}
