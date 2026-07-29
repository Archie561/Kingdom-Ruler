using System;
using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;
using KingdomRuler.Core;
using KingdomRuler.Shared.Ledger;
using KingdomRuler.Modules.Cities.Domain;
using KingdomRuler.Modules.Cities;

namespace KingdomRuler.Tests.EditMode.Modules.Cities
{
    [TestFixture]
    public class CityAffordabilityCheckerTests
    {
        private EventBus _eventBus;
        private KingdomLedger _ledger;

        [SetUp]
        public void SetUp()
        {
            _eventBus = new EventBus();
            _ledger = new KingdomLedger(_eventBus);
        }

        [Test]
        public void Check_AllRequirementsMet_ReturnsTrue()
        {
            _ledger.AddGold(100);
            _ledger.AddCharacteristicPoints(CharacteristicType.Army, 100, _ => 50);
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 20000f);
            _ledger.AddTradeResource(TradeResourceType.Wood, 50);

            var charReqs = new[] { new CharacteristicRequirement { Type = CharacteristicType.Army, RequiredLevel = 1 } };
            var resCosts = new[] { new ResourceCost { Type = TradeResourceType.Wood, Amount = 50 } };

            var (success, _) = CityAffordabilityChecker.Check(_ledger, charReqs, resCosts, 100);
            
            Assert.IsTrue(success);
        }

        [Test]
        public void Check_CharacteristicLevelTooLow_ReturnsFalse()
        {
            var charReqs = new[] { new CharacteristicRequirement { Type = CharacteristicType.Army, RequiredLevel = 2 } };
            
            var (success, _) = CityAffordabilityChecker.Check(_ledger, charReqs, Array.Empty<ResourceCost>(), 0);
            
            Assert.IsFalse(success);
        }

        [Test]
        public void Check_GoldInsufficient_ReturnsFalse()
        {
            _ledger.AddGold(50);
            
            var (success, _) = CityAffordabilityChecker.Check(_ledger, Array.Empty<CharacteristicRequirement>(), Array.Empty<ResourceCost>(), 100);
            
            Assert.IsFalse(success);
        }

        [Test]
        public void Check_TradeResourceInsufficient_ReturnsFalse()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 20000f);
            _ledger.AddTradeResource(TradeResourceType.Wood, 20);

            var resCosts = new[] { new ResourceCost { Type = TradeResourceType.Wood, Amount = 50 } };

            var (success, _) = CityAffordabilityChecker.Check(_ledger, Array.Empty<CharacteristicRequirement>(), resCosts, 0);
            
            Assert.IsFalse(success);
        }

        [Test]
        public void Check_NoRequirements_ReturnsTrue()
        {
            var (success, _) = CityAffordabilityChecker.Check(_ledger, null, null, 0);
            
            Assert.IsTrue(success);
        }
    }

    [TestFixture]
    public class CitiesManagerTests
    {
        private EventBus _eventBus;
        private KingdomLedger _ledger;
        private CitiesManager _manager;
        private List<ScriptableObject> _createdObjects;
        private RegionDefinition _region;
        private CityDefinition _city1;
        private CityDefinition _city2;

        [SetUp]
        public void SetUp()
        {
            _createdObjects = new List<ScriptableObject>();
            _eventBus = new EventBus();
            _ledger = new KingdomLedger(_eventBus);

            _city1 = ScriptableObject.CreateInstance<CityDefinition>();
            _city1.CityId = "city1";
            _city1.RegionId = "region1";
            _city1.GoldCost = 100;
            _city1.CharacteristicRequirements = Array.Empty<CharacteristicRequirement>();
            _city1.TradeResourceCosts = new[] { new ResourceCost { Type = TradeResourceType.Wood, Amount = 50 } };
            _city1.PopulationGranted = 1000;
            _createdObjects.Add(_city1);

            _city2 = ScriptableObject.CreateInstance<CityDefinition>();
            _city2.CityId = "city2";
            _city2.RegionId = "region1";
            _city2.GoldCost = 200;
            _city2.CharacteristicRequirements = Array.Empty<CharacteristicRequirement>();
            _city2.TradeResourceCosts = Array.Empty<ResourceCost>();
            _city2.PopulationGranted = 2000;
            _createdObjects.Add(_city2);

            _region = ScriptableObject.CreateInstance<RegionDefinition>();
            _region.RegionId = "region1";
            _region.Cities = new[] { _city1, _city2 };
            _createdObjects.Add(_region);

            _manager = new CitiesManager(_ledger, _eventBus);
            _manager.InitializeRegions(new[] { _region });
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
        public void PurchaseCity_SucceedsAndDeductsResources()
        {
            _ledger.AddGold(100);
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 20000f);
            _ledger.AddTradeResource(TradeResourceType.Wood, 50);

            var success = _manager.PurchaseCity("city1");
            
            Assert.IsTrue(success);
            Assert.AreEqual(0, _ledger.Gold);
            Assert.AreEqual(0f, _ledger.GetTradeResource(TradeResourceType.Wood).Amount);
        }

        [Test]
        public void PurchaseCity_FailsWithoutResources()
        {
            _ledger.AddGold(100);
            
            var success = _manager.PurchaseCity("city1");
            
            Assert.IsFalse(success);
        }

        [Test]
        public void PurchaseCity_AddsToPopulation()
        {
            _ledger.AddGold(100);
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 20000f);
            _ledger.AddTradeResource(TradeResourceType.Wood, 50);

            _manager.PurchaseCity("city1");
            
            Assert.AreEqual(1000, _manager.TotalPopulation);
        }

        [Test]
        public void IsCityPurchased_ReturnsTrueAfterPurchase()
        {
            _ledger.AddGold(100);
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 20000f);
            _ledger.AddTradeResource(TradeResourceType.Wood, 50);

            _manager.PurchaseCity("city1");
            
            Assert.IsTrue(_manager.IsCityPurchased("city1"));
        }

        [Test]
        public void IsRegionComplete_ReturnsTrueWhenAllCitiesPurchased()
        {
            _ledger.AddGold(300);
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 20000f);
            _ledger.AddTradeResource(TradeResourceType.Wood, 50);

            _manager.PurchaseCity("city1");
            _manager.PurchaseCity("city2");
            
            Assert.IsTrue(_manager.IsRegionComplete("region1"));
        }

        [Test]
        public void IsRegionComplete_ReturnsFalseWhenSomeCitiesRemain()
        {
            _ledger.AddGold(100);
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 20000f);
            _ledger.AddTradeResource(TradeResourceType.Wood, 50);

            _manager.PurchaseCity("city1");
            
            Assert.IsFalse(_manager.IsRegionComplete("region1"));
        }

        [Test]
        public void PurchaseCity_AlreadyPurchased_Fails()
        {
            _ledger.AddGold(200);
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 20000f);
            _ledger.AddTradeResource(TradeResourceType.Wood, 100);

            var firstPurchase = _manager.PurchaseCity("city1");
            var secondPurchase = _manager.PurchaseCity("city1");
            
            Assert.IsTrue(firstPurchase);
            Assert.IsFalse(secondPurchase);
        }
    }
}
