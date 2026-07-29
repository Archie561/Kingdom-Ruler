using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using KingdomRuler.Core;
using KingdomRuler.Shared.Ledger;
using KingdomRuler.Shared.Services;
using KingdomRuler.Modules.Events;
using KingdomRuler.Modules.Events.Domain;

namespace KingdomRuler.Tests.EditMode.Modules.Events
{
    public class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; } = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public void Advance(TimeSpan duration) => UtcNow += duration;
    }

    [TestFixture]
    public class EventOutcomeEffectTests
    {
        [Test]
        public void GoldEffect_CreatesCorrectStruct()
        {
            var effect = EventOutcomeEffect.GoldEffect(150f);
            Assert.AreEqual(150f, effect.Amount);
            Assert.IsFalse(effect.IsPercentage);
        }

        [Test]
        public void CharacteristicEffect_CreatesCorrectStruct()
        {
            var effect = EventOutcomeEffect.CharacteristicEffect(CharacteristicType.Army, 10f);
            Assert.AreEqual(CharacteristicType.Army, effect.CharacteristicType);
            Assert.AreEqual(10f, effect.Amount);
            Assert.IsFalse(effect.IsPercentage);
        }

        [Test]
        public void TradeResourceEffect_CreatesCorrectStruct()
        {
            var effect = EventOutcomeEffect.TradeResourceEffect(TradeResourceType.Wood, -50f, true);
            Assert.AreEqual(TradeResourceType.Wood, effect.TradeResourceType);
            Assert.AreEqual(-50f, effect.Amount);
            Assert.IsTrue(effect.IsPercentage);
        }
    }

    [TestFixture]
    public class EventOutcomeApplierTests
    {
        private KingdomLedger _ledger;

        [SetUp]
        public void SetUp()
        {
            var eventBus = new EventBus();
            _ledger = new KingdomLedger(eventBus);
        }

        private float PointsRequired(int level) => 100f * level;

        [Test]
        public void Apply_GoldGain_AddsToLedger()
        {
            var effects = new[] { EventOutcomeEffect.GoldEffect(100f) };
            EventOutcomeApplier.Apply(_ledger, effects, PointsRequired);
            Assert.AreEqual(100f, _ledger.Gold);
        }

        [Test]
        public void Apply_GoldLoss_SpendsFromLedger()
        {
            _ledger.AddGold(200);
            var effects = new[] { EventOutcomeEffect.GoldEffect(-50f) };
            EventOutcomeApplier.Apply(_ledger, effects, PointsRequired);
            Assert.AreEqual(150f, _ledger.Gold);
        }

        [Test]
        public void Apply_CharacteristicGain_AddsPoints()
        {
            var effects = new[] { EventOutcomeEffect.CharacteristicEffect(CharacteristicType.Army, 50f) };
            EventOutcomeApplier.Apply(_ledger, effects, PointsRequired);
            Assert.AreEqual(50f, _ledger.GetCharacteristic(CharacteristicType.Army).PointsIntoCurrentLevel);
        }

        [Test]
        public void Apply_CharacteristicLoss_ReducesPointsRespectingFloor()
        {
            _ledger.AddCharacteristicPoints(CharacteristicType.Army, 50f, PointsRequired);
            var effects = new[] { EventOutcomeEffect.CharacteristicEffect(CharacteristicType.Army, -100f) };
            EventOutcomeApplier.Apply(_ledger, effects, PointsRequired);
            Assert.AreEqual(0f, _ledger.GetCharacteristic(CharacteristicType.Army).PointsIntoCurrentLevel);
        }

        [Test]
        public void Apply_TradeResourceGain_AddsToLedger()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 20000f);
            var effects = new[] { EventOutcomeEffect.TradeResourceEffect(TradeResourceType.Wood, 100f, false) };
            EventOutcomeApplier.Apply(_ledger, effects, PointsRequired);
            Assert.AreEqual(100f, _ledger.GetTradeResource(TradeResourceType.Wood).Amount);
        }

        [Test]
        public void Apply_TradeResourceLoss_SpendsFromLedger()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 20000f);
            _ledger.AddTradeResource(TradeResourceType.Wood, 200f);
            var effects = new[] { EventOutcomeEffect.TradeResourceEffect(TradeResourceType.Wood, -50f, false) };
            EventOutcomeApplier.Apply(_ledger, effects, PointsRequired);
            Assert.AreEqual(150f, _ledger.GetTradeResource(TradeResourceType.Wood).Amount);
        }

        [Test]
        public void Apply_TradeResourcePercentageLoss_CalculatesCorrectly()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 20000f);
            _ledger.AddTradeResource(TradeResourceType.Wood, 500f);
            
            // Lose 20%
            var effects = new[] { EventOutcomeEffect.TradeResourceEffect(TradeResourceType.Wood, -20f, true) };
            EventOutcomeApplier.Apply(_ledger, effects, PointsRequired);
            
            Assert.AreEqual(400f, _ledger.GetTradeResource(TradeResourceType.Wood).Amount);
        }

        [Test]
        public void Apply_NullEffectsArray_DoesNothing()
        {
            _ledger.AddGold(100);
            Assert.DoesNotThrow(() => EventOutcomeApplier.Apply(_ledger, null, PointsRequired));
            Assert.AreEqual(100f, _ledger.Gold);
        }
    }

    [TestFixture]
    public class WeightedEventSelectorTests
    {
        private EventDefinition _eventA;
        private EventDefinition _eventB;

        [SetUp]
        public void SetUp()
        {
            _eventA = ScriptableObject.CreateInstance<EventDefinition>();
            _eventA.Weight = 10;
            _eventB = ScriptableObject.CreateInstance<EventDefinition>();
            _eventB.Weight = 90;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_eventA);
            UnityEngine.Object.DestroyImmediate(_eventB);
        }

        [Test]
        public void Select_EmptyPool_ReturnsNull()
        {
            var rng = new System.Random(1);
            var result = WeightedEventSelector.Select(new List<EventDefinition>(), rng);
            Assert.IsNull(result);
        }

        [Test]
        public void Select_NullPool_ReturnsNull()
        {
            var rng = new System.Random(1);
            var result = WeightedEventSelector.Select(null, rng);
            Assert.IsNull(result);
        }

        [Test]
        public void Select_SingleItemPool_ReturnsItem()
        {
            var rng = new System.Random(1);
            var result = WeightedEventSelector.Select(new List<EventDefinition> { _eventA }, rng);
            Assert.AreEqual(_eventA, result);
        }

        [Test]
        public void Select_HigherWeightSelectedMoreOften()
        {
            var rng = new System.Random(42);
            var pool = new List<EventDefinition> { _eventA, _eventB };

            int countA = 0;
            int countB = 0;

            for (int i = 0; i < 1000; i++)
            {
                var result = WeightedEventSelector.Select(pool, rng);
                if (result == _eventA) countA++;
                else if (result == _eventB) countB++;
            }

            Assert.Greater(countB, countA);
            Assert.Greater(countA, 0); // Should be hit at least once
        }

        [Test]
        public void Select_AllZeroWeights_ReturnsNull()
        {
            var rng = new System.Random(1);
            _eventA.Weight = 0;
            _eventB.Weight = 0;
            var pool = new List<EventDefinition> { _eventA, _eventB };
            var result = WeightedEventSelector.Select(pool, rng);
            Assert.IsNull(result);
        }
    }

    [TestFixture]
    public class EventsManagerTests
    {
        private EventBus _eventBus;
        private KingdomLedger _ledger;
        private FakeClock _clock;
        private EventsConfig _config;
        private EventsManager _manager;

        private EventDefinition _eventA;
        private EventDefinition _eventB;

        [SetUp]
        public void SetUp()
        {
            _eventBus = new EventBus();
            _ledger = new KingdomLedger(_eventBus);
            _clock = new FakeClock();
            
            _config = ScriptableObject.CreateInstance<EventsConfig>();
            _config.SpawnIntervalSeconds = 3600;
            _config.MaxPendingEvents = 5;

            _manager = new EventsManager(_ledger, _eventBus, _clock, _config);

            _eventA = ScriptableObject.CreateInstance<EventDefinition>();
            _eventA.EventId = "EventA";
            _eventA.Weight = 100;
            _eventA.ChoiceA = new EventChoice 
            { 
                ChoiceTextKey = "ChoiceA1", 
                Effects = new[] { EventOutcomeEffect.GoldEffect(100f) } 
            };
            _eventA.ChoiceB = new EventChoice 
            { 
                ChoiceTextKey = "ChoiceA2", 
                Effects = new[] { EventOutcomeEffect.GoldEffect(200f) } 
            };

            _eventB = ScriptableObject.CreateInstance<EventDefinition>();
            _eventB.EventId = "EventB";
            _eventB.Weight = 100;
            _eventB.ChoiceA = new EventChoice 
            { 
                ChoiceTextKey = "ChoiceB1", 
                Effects = new[] { EventOutcomeEffect.GoldEffect(-50f) } 
            };
            _eventB.ChoiceB = new EventChoice 
            { 
                ChoiceTextKey = "ChoiceB2", 
                Effects = new[] { EventOutcomeEffect.GoldEffect(0f) } 
            };

            _manager.InitializeEventPool(new[] { _eventA, _eventB });
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_config);
            UnityEngine.Object.DestroyImmediate(_eventA);
            UnityEngine.Object.DestroyImmediate(_eventB);
        }

        [Test]
        public void ProcessSpawning_AddsEventAfterInterval()
        {
            Assert.AreEqual(0, _manager.PendingCount);
            _clock.Advance(TimeSpan.FromSeconds(3600));
            _manager.ProcessSpawning();
            Assert.AreEqual(1, _manager.PendingCount);
        }

        [Test]
        public void ProcessSpawning_CapsAtMaxPendingEvents()
        {
            _clock.Advance(TimeSpan.FromSeconds(3600 * 10)); // 10 hours
            _manager.ProcessSpawning();
            Assert.AreEqual(5, _manager.PendingCount); // Capped at MaxPendingEvents=5
        }

        [Test]
        public void ProcessSpawning_HandlesMultipleIntervalsOffline()
        {
            _clock.Advance(TimeSpan.FromSeconds(3600 * 3)); // 3 hours
            _manager.ProcessSpawning();
            Assert.AreEqual(3, _manager.PendingCount);
        }

        [Test]
        public void ResolveEvent_ChoiceIndex0_AppliesChoiceAEffects()
        {
            _manager.InitializeEventPool(new[] { _eventA });
            _clock.Advance(TimeSpan.FromSeconds(3600));
            _manager.ProcessSpawning();

            bool result = _manager.ResolveEvent(0, 0);
            
            Assert.IsTrue(result);
            Assert.AreEqual(100f, _ledger.Gold);
        }

        [Test]
        public void ResolveEvent_ChoiceIndex1_AppliesChoiceBEffects()
        {
            _manager.InitializeEventPool(new[] { _eventA });
            _clock.Advance(TimeSpan.FromSeconds(3600));
            _manager.ProcessSpawning();

            bool result = _manager.ResolveEvent(0, 1);
            
            Assert.IsTrue(result);
            Assert.AreEqual(200f, _ledger.Gold);
        }

        [Test]
        public void ResolveEvent_RemovesEventFromPending()
        {
            _clock.Advance(TimeSpan.FromSeconds(3600));
            _manager.ProcessSpawning();
            Assert.AreEqual(1, _manager.PendingCount);

            _manager.ResolveEvent(0, 0);
            
            Assert.AreEqual(0, _manager.PendingCount);
        }

        [Test]
        public void ResolveEvent_InvalidEventIndex_ReturnsFalse()
        {
            _clock.Advance(TimeSpan.FromSeconds(3600));
            _manager.ProcessSpawning();

            bool result = _manager.ResolveEvent(99, 0);
            
            Assert.IsFalse(result);
            Assert.AreEqual(1, _manager.PendingCount);
        }

        [Test]
        public void ResolveEvent_InvalidChoiceIndex_ReturnsFalse()
        {
            _clock.Advance(TimeSpan.FromSeconds(3600));
            _manager.ProcessSpawning();

            bool result = _manager.ResolveEvent(0, 99);
            
            Assert.IsFalse(result);
            Assert.AreEqual(1, _manager.PendingCount);
        }

        [Test]
        public void PendingCount_ReturnsCorrectValue()
        {
            Assert.AreEqual(0, _manager.PendingCount);
            
            _clock.Advance(TimeSpan.FromSeconds(3600));
            _manager.ProcessSpawning();
            Assert.AreEqual(1, _manager.PendingCount);
            
            _manager.ResolveEvent(0, 0);
            Assert.AreEqual(0, _manager.PendingCount);
        }
    }
}
