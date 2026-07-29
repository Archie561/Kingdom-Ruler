using System;
using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;
using KingdomRuler.Core;
using KingdomRuler.Shared.Ledger;
using KingdomRuler.Shared.Services;
using KingdomRuler.Modules.Economy.Domain;
using KingdomRuler.Modules.Economy;

namespace KingdomRuler.Tests.EditMode.Modules.Economy
{
    public class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; } = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public void Advance(TimeSpan duration) => UtcNow += duration;
    }

    [TestFixture]
    public class BusinessCostCalculatorTests
    {
        [Test]
        public void CalculateCost_WithZeroOwned_ReturnsBaseCost()
        {
            var cost = BusinessCostCalculator.CalculateCost(100, 0);
            Assert.AreEqual(100, cost);
        }

        [Test]
        public void CalculateCost_WithOwned_IncreasesCost()
        {
            var cost = BusinessCostCalculator.CalculateCost(100, 5, 1.15f);
            Assert.AreEqual(202, cost);
        }

        [Test]
        public void CalculateCost_NegativeBaseCost_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => BusinessCostCalculator.CalculateCost(-100, 0));
        }

        [Test]
        public void CalculateCost_NegativeCount_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => BusinessCostCalculator.CalculateCost(100, -1));
        }
    }

    [TestFixture]
    public class BusinessAccrualCalculatorTests
    {
        [Test]
        public void StorageCap_ReturnsCorrectAmount()
        {
            var cap = BusinessAccrualCalculator.StorageCap(2, 10);
            Assert.AreEqual(28800, cap);
        }

        [Test]
        public void Accrue_UnderCap_ReturnsCorrectAmount()
        {
            var lastAccrued = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var now = lastAccrued.AddMinutes(60);
            
            var accrued = BusinessAccrualCalculator.Accrue(0, 1, 10, lastAccrued, now);
            Assert.AreEqual(600, accrued);
        }

        [Test]
        public void Accrue_ExceedsCap_ReturnsCap()
        {
            var lastAccrued = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var now = lastAccrued.AddDays(2);
            
            var accrued = BusinessAccrualCalculator.Accrue(0, 1, 10, lastAccrued, now);
            Assert.AreEqual(14400, accrued);
        }

        [Test]
        public void Accrue_WithZeroCount_ReturnsCurrentStored()
        {
            var lastAccrued = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var now = lastAccrued.AddMinutes(60);
            
            var accrued = BusinessAccrualCalculator.Accrue(50, 0, 10, lastAccrued, now);
            Assert.AreEqual(50, accrued);
        }

        [Test]
        public void Accrue_ZeroElapsed_ReturnsCurrentStored()
        {
            var lastAccrued = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            
            var accrued = BusinessAccrualCalculator.Accrue(50, 1, 10, lastAccrued, lastAccrued);
            Assert.AreEqual(50, accrued);
        }
    }

    [TestFixture]
    public class EconomyManagerTests
    {
        private EventBus _eventBus;
        private KingdomLedger _ledger;
        private FakeClock _clock;
        private EconomyConfig _config;
        private EconomyManager _manager;
        private BusinessDefinition _quarryDef;
        private List<ScriptableObject> _createdObjects;

        [SetUp]
        public void SetUp()
        {
            _createdObjects = new List<ScriptableObject>();
            _eventBus = new EventBus();
            _ledger = new KingdomLedger(_eventBus);
            _clock = new FakeClock();
            
            _config = ScriptableObject.CreateInstance<EconomyConfig>();
            _createdObjects.Add(_config);

            _quarryDef = ScriptableObject.CreateInstance<BusinessDefinition>();
            _quarryDef.BusinessId = "quarry";
            _quarryDef.BaseCost = 100;
            _quarryDef.CostMultiplier = 1.15f;
            _quarryDef.BaseGoldPerMinute = 10;
            _createdObjects.Add(_quarryDef);

            _manager = new EconomyManager(_ledger, _eventBus, _clock, _config);
            _manager.InitializeBusinesses(new[] { _quarryDef });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _createdObjects)
            {
                if (obj != null)
                {
                    ScriptableObject.DestroyImmediate(obj);
                }
            }
        }

        [Test]
        public void BuyBusiness_WithSufficientGold_SucceedsAndIncreasesCount()
        {
            _ledger.AddGold(100);
            var success = _manager.BuyBusiness("quarry");
            
            Assert.IsTrue(success);
            Assert.AreEqual(0, _ledger.Gold);
            Assert.AreEqual(1, _manager.GetCountOwned("quarry"));
        }

        [Test]
        public void BuyBusiness_WithInsufficientGold_Fails()
        {
            _ledger.AddGold(50);
            var success = _manager.BuyBusiness("quarry");
            
            Assert.IsFalse(success);
            Assert.AreEqual(50, _ledger.Gold);
            Assert.AreEqual(0, _manager.GetCountOwned("quarry"));
        }

        [Test]
        public void CollectGold_ReturnsAccruedGoldAfterTimePasses()
        {
            _ledger.AddGold(100);
            _manager.BuyBusiness("quarry");
            
            _clock.Advance(TimeSpan.FromMinutes(10));
            _manager.ProcessAccrual();
            
            var collected = _manager.CollectGold("quarry");
            Assert.AreEqual(100, collected);
        }

        [Test]
        public void CollectGold_AddsToLedgerGold()
        {
            _ledger.AddGold(100);
            _manager.BuyBusiness("quarry");
            
            _clock.Advance(TimeSpan.FromMinutes(10));
            _manager.ProcessAccrual();
            _manager.CollectGold("quarry");
            
            Assert.AreEqual(100, _ledger.Gold);
        }

        [Test]
        public void GetNextPurchaseCost_IncreasesAfterPurchase()
        {
            _ledger.AddGold(1000);
            
            var initialCost = _manager.GetNextPurchaseCost("quarry");
            Assert.AreEqual(100, initialCost);
            
            _manager.BuyBusiness("quarry");
            
            var nextCost = _manager.GetNextPurchaseCost("quarry");
            Assert.AreEqual(115, nextCost);
        }

        [Test]
        public void ProcessAccrual_UpdatesStoredGold()
        {
            _ledger.AddGold(100);
            _manager.BuyBusiness("quarry");
            
            _clock.Advance(TimeSpan.FromMinutes(5));
            _manager.ProcessAccrual();
            
            Assert.AreEqual(50, _manager.GetStoredGold("quarry"));
        }
    }
}
