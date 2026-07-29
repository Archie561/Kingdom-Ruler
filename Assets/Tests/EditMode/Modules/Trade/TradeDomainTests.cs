using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using KingdomRuler.Core;
using KingdomRuler.Shared.Ledger;
using KingdomRuler.Shared.Services;
using KingdomRuler.Modules.Trade;
using KingdomRuler.Modules.Trade.Domain;

namespace KingdomRuler.Tests.EditMode.Modules.Trade
{
    public class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; } = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public void Advance(TimeSpan duration) => UtcNow += duration;
    }

    [TestFixture]
    public class TradeResourcePairTests
    {
        [Test]
        public void GetPairedResource_ReturnsCorrectPairs()
        {
            Assert.AreEqual(TradeResourceType.Wood, TradeResourcePair.GetPairedResource(TradeResourceType.Stone));
            Assert.AreEqual(TradeResourceType.Stone, TradeResourcePair.GetPairedResource(TradeResourceType.Wood));
            
            Assert.AreEqual(TradeResourceType.Minerals, TradeResourcePair.GetPairedResource(TradeResourceType.Metal));
            Assert.AreEqual(TradeResourceType.Metal, TradeResourcePair.GetPairedResource(TradeResourceType.Minerals));
            
            Assert.AreEqual(TradeResourceType.Clay, TradeResourcePair.GetPairedResource(TradeResourceType.Leather));
            Assert.AreEqual(TradeResourceType.Leather, TradeResourcePair.GetPairedResource(TradeResourceType.Clay));
        }
    }

    [TestFixture]
    public class WarehouseUpgradeCalculatorTests
    {
        [Test]
        public void ResourcePathCost_Returns80PercentOfCapacity()
        {
            Assert.AreEqual(80f, WarehouseUpgradeCalculator.ResourcePathCost(100f));
            Assert.AreEqual(400f, WarehouseUpgradeCalculator.ResourcePathCost(500f));
        }

        [Test]
        public void CrystalPathCost_FromCurve_ValidIndex()
        {
            var curve = new int[] { 5, 10, 15, 20 };
            Assert.AreEqual(10, WarehouseUpgradeCalculator.CrystalPathCost(1, curve));
        }

        [Test]
        public void CrystalPathCost_BeyondCurve_UsesFallback()
        {
            var curve = new int[] { 5, 10 };
            Assert.DoesNotThrow(() => WarehouseUpgradeCalculator.CrystalPathCost(5, curve));
        }

        [Test]
        public void CrystalPathCost_NullCurve_UsesFallback()
        {
            Assert.DoesNotThrow(() => WarehouseUpgradeCalculator.CrystalPathCost(0, null));
        }

        [Test]
        public void CapacityAtLevel_FromCurve_ValidIndex()
        {
            var curve = new float[] { 100f, 200f, 400f };
            Assert.AreEqual(200f, WarehouseUpgradeCalculator.CapacityAtLevel(1, curve));
        }

        [Test]
        public void CapacityAtLevel_BeyondCurve_UsesFallback()
        {
            var curve = new float[] { 100f, 200f };
            Assert.DoesNotThrow(() => WarehouseUpgradeCalculator.CapacityAtLevel(5, curve));
        }
    }

    [TestFixture]
    public class TradeOfferTests
    {
        [Test]
        public void TotalGive_SumsGiveResources()
        {
            var give = new Dictionary<TradeResourceType, float>
            {
                { TradeResourceType.Stone, 10f },
                { TradeResourceType.Wood, 20f }
            };
            var receive = new Dictionary<TradeResourceType, float>();
            var offer = new TradeOffer("test", give, receive, TradeProfitability.Neutral);
            
            Assert.AreEqual(30f, offer.TotalGive());
        }

        [Test]
        public void TotalReceive_SumsReceiveResources()
        {
            var give = new Dictionary<TradeResourceType, float>();
            var receive = new Dictionary<TradeResourceType, float>
            {
                { TradeResourceType.Metal, 15f },
                { TradeResourceType.Leather, 25f }
            };
            var offer = new TradeOffer("test", give, receive, TradeProfitability.Neutral);
            
            Assert.AreEqual(40f, offer.TotalReceive());
        }
    }

    [TestFixture]
    public class TradeOfferGeneratorTests
    {
        [Test]
        public void GenerateBatch_ReturnsCorrectCount()
        {
            var generator = new TradeOfferGenerator();
            var offers = generator.GenerateBatch(5);
            Assert.AreEqual(5, offers.Count);
        }

        [Test]
        public void GenerateBatch_HasCorrectProfitabilityDistribution()
        {
            var generator = new TradeOfferGenerator(12345);
            var offers = generator.GenerateBatch(100);
            
            Assert.AreEqual(100, offers.Count);
            Assert.IsTrue(offers.Any(o => o.Profitability == TradeProfitability.Profitable));
            Assert.IsTrue(offers.Any(o => o.Profitability == TradeProfitability.Neutral));
            Assert.IsTrue(offers.Any(o => o.Profitability == TradeProfitability.Unprofitable));
        }

        [Test]
        public void AllOffers_HaveNonOverlappingGiveReceiveResourceTypes()
        {
            var generator = new TradeOfferGenerator();
            var offers = generator.GenerateBatch(10);
            
            foreach (var offer in offers)
            {
                var giveTypes = offer.GiveResources.Keys.ToList();
                var receiveTypes = offer.ReceiveResources.Keys.ToList();
                var overlap = giveTypes.Intersect(receiveTypes);
                Assert.IsEmpty(overlap, "Give and Receive types should not overlap");
            }
        }

        [Test]
        public void AllOffers_Have2to3GiveAndReceiveTypes()
        {
            var generator = new TradeOfferGenerator();
            var offers = generator.GenerateBatch(10);
            
            foreach (var offer in offers)
            {
                Assert.IsTrue(offer.GiveResources.Count >= 2 && offer.GiveResources.Count <= 3);
                Assert.IsTrue(offer.ReceiveResources.Count >= 2 && offer.ReceiveResources.Count <= 3);
            }
        }

        [Test]
        public void AllOffers_ResourceAmountsArePositive()
        {
            var generator = new TradeOfferGenerator();
            var offers = generator.GenerateBatch(10);
            
            foreach (var offer in offers)
            {
                foreach (var amt in offer.GiveResources.Values) Assert.Greater(amt, 0f);
                foreach (var amt in offer.ReceiveResources.Values) Assert.Greater(amt, 0f);
            }
        }
    }

    [TestFixture]
    public class TradeManagerTests
    {
        private EventBus _eventBus;
        private KingdomLedger _ledger;
        private FakeClock _clock;
        private TradeConfig _config;
        private TradeManager _manager;

        [SetUp]
        public void Setup()
        {
            _eventBus = new EventBus();
            _ledger = new KingdomLedger(_eventBus);
            _clock = new FakeClock();
            
            _config = ScriptableObject.CreateInstance<TradeConfig>();
            _config.OfferCount = 10;
            _config.OfferRefreshTimeSeconds = 1200; // 20 min
            _config.InstantRefreshCrystalCost = 3;
            _config.OfferBaseAmount = 50;

            _manager = new TradeManager(_ledger, _eventBus, _clock, _config);
        }

        [TearDown]
        public void Teardown()
        {
            if (_config != null)
            {
                ScriptableObject.DestroyImmediate(_config);
            }
        }

        [Test]
        public void Constructor_GeneratesInitialOffers()
        {
            Assert.AreEqual(10, _manager.ActiveOffers.Count);
        }

        [Test]
        public void ProcessOfferRefresh_DoesNothingBefore20Min()
        {
            var firstOfferId = _manager.ActiveOffers[0].Id;
            
            _clock.Advance(TimeSpan.FromMinutes(10));
            _manager.ProcessOfferRefresh();
            
            Assert.AreEqual(firstOfferId, _manager.ActiveOffers[0].Id);
        }

        [Test]
        public void ProcessOfferRefresh_RegeneratesOffersAfter20Min()
        {
            var firstOfferId = _manager.ActiveOffers[0].Id;
            
            _clock.Advance(TimeSpan.FromMinutes(21));
            _manager.ProcessOfferRefresh();
            
            Assert.AreNotEqual(firstOfferId, _manager.ActiveOffers[0].Id);
        }

        [Test]
        public void InstantRefresh_SpendsCrystalsAndRegenerates()
        {
            _ledger.AddCrystals(10);
            var firstOfferId = _manager.ActiveOffers[0].Id;
            
            var success = _manager.InstantRefresh();
            
            Assert.IsTrue(success);
            Assert.AreEqual(7, _ledger.Crystals);
            Assert.AreNotEqual(firstOfferId, _manager.ActiveOffers[0].Id);
        }

        [Test]
        public void InstantRefresh_FailsWithoutEnoughCrystals()
        {
            _ledger.AddCrystals(2); // needs 3
            var firstOfferId = _manager.ActiveOffers[0].Id;
            
            var success = _manager.InstantRefresh();
            
            Assert.IsFalse(success);
            Assert.AreEqual(2, _ledger.Crystals);
            Assert.AreEqual(firstOfferId, _manager.ActiveOffers[0].Id);
        }

        [Test]
        public void GetSecondsUntilRefresh_ReturnsCorrectRemainingTime()
        {
            _clock.Advance(TimeSpan.FromSeconds(200));
            Assert.AreEqual(1000f, _manager.GetSecondsUntilRefresh());
        }

        [Test]
        public void AcceptOffer_SpendsGiveResourcesAndAddsReceiveResources()
        {
            // Increase warehouse capacity and add resources so any offer can be accepted
            foreach (TradeResourceType type in Enum.GetValues(typeof(TradeResourceType)))
            {
                _ledger.SetWarehouseCapacity(type, 20000f);
                _ledger.AddTradeResource(type, 10000f);
            }

            var offer = _manager.ActiveOffers[0];
            var giveType = offer.GiveResources.Keys.First();
            var receiveType = offer.ReceiveResources.Keys.First();
            var giveAmount = offer.GiveResources[giveType];
            var receiveAmount = offer.ReceiveResources[receiveType];

            var success = _manager.AcceptOffer(0);
            
            Assert.IsTrue(success);
            Assert.AreEqual(10000f - giveAmount, _ledger.GetTradeResource(giveType).Amount);
            Assert.AreEqual(10000f + receiveAmount, _ledger.GetTradeResource(receiveType).Amount);
        }

        [Test]
        public void AcceptOffer_FailsIfPlayerLacksResources()
        {
            // No resources in ledger
            var success = _manager.AcceptOffer(0);
            
            Assert.IsFalse(success);
        }

        [Test]
        public void AcceptOffer_RemovesOfferFromActiveList()
        {
            foreach (TradeResourceType type in Enum.GetValues(typeof(TradeResourceType)))
            {
                _ledger.SetWarehouseCapacity(type, 20000f);
                _ledger.AddTradeResource(type, 10000f);
            }
            
            var initialCount = _manager.ActiveOffers.Count;
            var offer = _manager.ActiveOffers[0];
            
            var success = _manager.AcceptOffer(0);
            
            Assert.IsTrue(success);
            Assert.AreEqual(initialCount - 1, _manager.ActiveOffers.Count);
            Assert.IsFalse(_manager.ActiveOffers.Contains(offer));
        }

        [Test]
        public void AcceptOffer_WithInvalidIndex_ReturnsFalse()
        {
            var success = _manager.AcceptOffer(999);
            Assert.IsFalse(success);
        }

        [Test]
        public void UpgradeWarehouseWithCrystals_SpendsCrystalsAndIncreasesCapacity()
        {
            _ledger.AddCrystals(100);
            var initialCapacity = _ledger.GetTradeResource(TradeResourceType.Wood).Capacity;
            
            var success = _manager.UpgradeWarehouseWithCrystals(TradeResourceType.Wood);
            
            Assert.IsTrue(success);
            Assert.Less(_ledger.Crystals, 100);
            Assert.Greater(_ledger.GetTradeResource(TradeResourceType.Wood).Capacity, initialCapacity);
            Assert.AreEqual(1, _manager.GetWarehouseLevel(TradeResourceType.Wood));
        }

        [Test]
        public void UpgradeWarehouseWithCrystals_FailsWithoutEnoughCrystals()
        {
            // No crystals
            var initialCapacity = _ledger.GetTradeResource(TradeResourceType.Wood).Capacity;
            
            var success = _manager.UpgradeWarehouseWithCrystals(TradeResourceType.Wood);
            
            Assert.IsFalse(success);
            Assert.AreEqual(initialCapacity, _ledger.GetTradeResource(TradeResourceType.Wood).Capacity);
            Assert.AreEqual(0, _manager.GetWarehouseLevel(TradeResourceType.Wood));
        }

        [Test]
        public void UpgradeWarehouseWithResources_SpendsPairedResourceAndIncreasesCapacity()
        {
            // Wood's paired resource is Stone.
            _ledger.AddTradeResource(TradeResourceType.Stone, 10000f);
            var initialCapacity = _ledger.GetTradeResource(TradeResourceType.Wood).Capacity;
            
            var success = _manager.UpgradeWarehouseWithResources(TradeResourceType.Wood);
            
            Assert.IsTrue(success);
            Assert.Less(_ledger.GetTradeResource(TradeResourceType.Stone).Amount, 10000f);
            Assert.Greater(_ledger.GetTradeResource(TradeResourceType.Wood).Capacity, initialCapacity);
            Assert.AreEqual(1, _manager.GetWarehouseLevel(TradeResourceType.Wood));
        }

        [Test]
        public void UpgradeWarehouseWithResources_FailsWithoutEnoughPairedResource()
        {
            // No Stone
            var initialCapacity = _ledger.GetTradeResource(TradeResourceType.Wood).Capacity;
            
            var success = _manager.UpgradeWarehouseWithResources(TradeResourceType.Wood);
            
            Assert.IsFalse(success);
            Assert.AreEqual(initialCapacity, _ledger.GetTradeResource(TradeResourceType.Wood).Capacity);
            Assert.AreEqual(0, _manager.GetWarehouseLevel(TradeResourceType.Wood));
        }

        [Test]
        public void GetWarehouseLevel_StartsAt0AndIncrementsAfterUpgrade()
        {
            Assert.AreEqual(0, _manager.GetWarehouseLevel(TradeResourceType.Stone));
            
            _ledger.AddCrystals(100);
            _manager.UpgradeWarehouseWithCrystals(TradeResourceType.Stone);
            
            Assert.AreEqual(1, _manager.GetWarehouseLevel(TradeResourceType.Stone));
            
            _manager.UpgradeWarehouseWithCrystals(TradeResourceType.Stone);
            Assert.AreEqual(2, _manager.GetWarehouseLevel(TradeResourceType.Stone));
        }
    }
}
