using NUnit.Framework;
using UnityEngine;
using KingdomRuler.Modules.Trade;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Tests.EditMode.Modules.Trade
{
    /// <summary>The two warehouse formulas: capacity at a level, and the crystal price of the next upgrade.</summary>
    [TestFixture]
    public sealed class WarehouseCurveTests
    {
        private TradeConfig _config;

        [SetUp]
        public void SetUp() => _config = ScriptableObject.CreateInstance<TradeConfig>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_config);

        private int Capacity(int level)    => WarehouseCurve.Capacity(_config, level);
        private int CrystalCost(int level) => WarehouseCurve.CrystalCost(_config, level);

        // ── The cap ───────────────────────────────────────────────────────────────

        [Test]
        public void TopLevel_IsTheCapAsThePlayerSeesIt_LessOne()
        {
            // Stored levels count upgrades from 0 and the screen adds 1, so "max level 20" is stored 19.
            Assert.AreEqual(19, WarehouseCurve.TopLevel(_config));

            _config.MaxWarehouseLevel = 1;
            Assert.AreEqual(0, WarehouseCurve.TopLevel(_config), "A cap of 1 allows no upgrades.");

            _config.MaxWarehouseLevel = 0;   // a hand-edited asset; OnValidate stops a designer typing it
            Assert.AreEqual(0, WarehouseCurve.TopLevel(_config));
        }

        // ── Capacity ──────────────────────────────────────────────────────────────

        [Test]
        public void Capacity_AtLevel0_IsTheBase()
        {
            Assert.AreEqual(100, Capacity(0));
        }

        [Test]
        public void Capacity_UsesBankersRounding_MatchingTheCharacteristicLevelingCurve()
        {
            // 100 × 1.5² = 225 → 22.5 → ToEven 22 → 220, not 230. The same expression as the
            // sibling curve, deliberately; do not "fix".
            Assert.AreEqual(220, Capacity(2));
        }

        [Test]
        public void Capacity_MatchesTheWorkedNumbers()
        {
            // docs/plans/trade-rebuild-plan.md §4.2 and the offer sizing are worked from these.
            Assert.AreEqual(760,     Capacity(5));
            Assert.AreEqual(5770,    Capacity(10));
            Assert.AreEqual(221_680, Capacity(19));   // the top level, shown as 20
        }

        [Test]
        public void Capacity_IsStrictlyIncreasing_UpToTheCap()
        {
            for (int level = 1; level <= WarehouseCurve.TopLevel(_config); level++)
                Assert.Greater(Capacity(level), Capacity(level - 1), $"Capacity did not grow at level {level}.");
        }

        [Test]
        public void AnAbsurdLevel_IsClampedToTheConfiguredCap()
        {
            // An edited save must degrade to the biggest real warehouse, not to an overflowed number.
            Assert.AreEqual(Capacity(WarehouseCurve.TopLevel(_config)), Capacity(100_000));
        }

        [Test]
        public void Capacity_ANegativeLevel_IsTreatedAsLevel0()
        {
            Assert.AreEqual(Capacity(0), Capacity(-5));
        }

        [Test]
        public void Capacity_NeverReturnsZero_WhateverTheAssetHolds()
        {
            // A zero capacity is a warehouse that never refills. OnValidate stops a designer typing
            // these; a hand-edited asset bypasses it.
            _config.CapacityBase    = 0f;
            _config.CapacityGrowth  = 0.5f;   // shrinking, so it rounds toward zero
            _config.CapacityRoundTo = 0f;

            for (int level = 0; level <= WarehouseCurve.TopLevel(_config); level++)
                Assert.Greater(Capacity(level), 0, $"Zero at level {level}.");
        }

        [Test]
        public void Capacity_NeverExceedsTheLedgerCeiling_WhateverTheAssetHolds()
        {
            _config.CapacityGrowth    = 10f;
            _config.MaxWarehouseLevel = 50;

            Assert.AreEqual(KingdomLedger.MaxWarehouseCapacity, Capacity(49));
        }

        // ── Crystal price ─────────────────────────────────────────────────────────

        [Test]
        public void CrystalCost_FirstLevelsAre_5_8_11_17_25()
        {
            Assert.AreEqual(5,  CrystalCost(0));
            Assert.AreEqual(8,  CrystalCost(1));   // 7.5 → 8 (to even)
            Assert.AreEqual(11, CrystalCost(2));   // 11.25
            Assert.AreEqual(17, CrystalCost(3));   // 16.875
            Assert.AreEqual(25, CrystalCost(4));   // 25.3
        }

        [Test]
        public void CrystalCost_IsStrictlyIncreasing_UpToTheCap()
        {
            for (int level = 1; level < WarehouseCurve.TopLevel(_config); level++)
                Assert.Greater(CrystalCost(level), CrystalCost(level - 1), $"Cost did not rise at level {level}.");
        }

        [Test]
        public void CrystalCost_IsAlwaysAtLeastOne()
        {
            // A free upgrade would be an infinite capacity loop.
            _config.CrystalCostBase   = 0.001f;
            _config.CrystalCostGrowth = 1.01f;

            for (int level = 0; level <= WarehouseCurve.TopLevel(_config); level++)
                Assert.GreaterOrEqual(CrystalCost(level), 1);
        }

        [Test]
        public void CrystalCost_StaysPositiveAtAnAbsurdTuning()
        {
            // 1000 × 10^50 is far past int.MaxValue; wrapping negative would make the upgrade pay the player.
            _config.CrystalCostBase   = 1000f;
            _config.CrystalCostGrowth = 10f;
            _config.MaxWarehouseLevel = 50;

            Assert.AreEqual(int.MaxValue, CrystalCost(49));
        }
    }
}
