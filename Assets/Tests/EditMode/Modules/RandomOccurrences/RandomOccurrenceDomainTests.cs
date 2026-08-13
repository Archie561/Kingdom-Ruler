using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using KingdomRuler.Core;
using KingdomRuler.Shared.Ledger;
using KingdomRuler.Shared.Services;
using KingdomRuler.Modules.RandomOccurrences;
using KingdomRuler.Modules.RandomOccurrences.Domain;

namespace KingdomRuler.Tests.EditMode.Modules.RandomOccurrences
{
    public class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; } = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public void Advance(TimeSpan duration) => UtcNow += duration;
    }

    [TestFixture]
    public class RandomOccurrenceOutcomeEffectTests
    {
        [Test]
        public void GoldEffect_CreatesCorrectStruct()
        {
            var effect = RandomOccurrenceOutcomeEffect.GoldEffect(150f);
            Assert.AreEqual(150f, effect.Amount);
            Assert.IsFalse(effect.IsPercentage);
        }

        [Test]
        public void CharacteristicEffect_CreatesCorrectStruct()
        {
            var effect = RandomOccurrenceOutcomeEffect.CharacteristicEffect(CharacteristicType.Army, 10f);
            Assert.AreEqual(CharacteristicType.Army, effect.CharacteristicType);
            Assert.AreEqual(10f, effect.Amount);
            Assert.IsFalse(effect.IsPercentage);
        }

        [Test]
        public void TradeResourceEffect_CreatesCorrectStruct()
        {
            var effect = RandomOccurrenceOutcomeEffect.TradeResourceEffect(TradeResourceType.Wood, -50f, true);
            Assert.AreEqual(TradeResourceType.Wood, effect.TradeResourceType);
            Assert.AreEqual(-50f, effect.Amount);
            Assert.IsTrue(effect.IsPercentage);
        }
    }

    [TestFixture]
    public class RandomOccurrenceOutcomeApplierTests
    {
        private KingdomLedger _ledger;

        [SetUp]
        public void SetUp()
        {
            var eventBus = new EventBus();
            _ledger = new KingdomLedger(eventBus);
        }

        // No local curve: thresholds come from the Ledger, identical to every other mechanic.

        [Test]
        public void Apply_GoldGain_AddsToLedger()
        {
            var effects = new[] { RandomOccurrenceOutcomeEffect.GoldEffect(100f) };
            RandomOccurrenceOutcomeApplier.Apply(_ledger, effects);
            Assert.AreEqual(100f, _ledger.Gold);
        }

        [Test]
        public void Apply_GoldLoss_SpendsFromLedger()
        {
            _ledger.AddGold(200);
            var effects = new[] { RandomOccurrenceOutcomeEffect.GoldEffect(-50f) };
            RandomOccurrenceOutcomeApplier.Apply(_ledger, effects);
            Assert.AreEqual(150f, _ledger.Gold);
        }

        [Test]
        public void Apply_CharacteristicGain_AddsPoints()
        {
            var effects = new[] { RandomOccurrenceOutcomeEffect.CharacteristicEffect(CharacteristicType.Army, 50f) };
            RandomOccurrenceOutcomeApplier.Apply(_ledger, effects);
            Assert.AreEqual(50f, _ledger.GetCharacteristic(CharacteristicType.Army).PointsIntoCurrentLevel);
        }

        [Test]
        public void Apply_CharacteristicLoss_ReducesPointsRespectingFloor()
        {
            _ledger.AddCharacteristicPoints(CharacteristicType.Army, 50f);
            var effects = new[] { RandomOccurrenceOutcomeEffect.CharacteristicEffect(CharacteristicType.Army, -100f) };
            RandomOccurrenceOutcomeApplier.Apply(_ledger, effects);
            Assert.AreEqual(0f, _ledger.GetCharacteristic(CharacteristicType.Army).PointsIntoCurrentLevel);
        }

        [Test]
        public void Apply_TradeResourceGain_AddsToLedger()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 20000f);
            var effects = new[] { RandomOccurrenceOutcomeEffect.TradeResourceEffect(TradeResourceType.Wood, 100f, false) };
            RandomOccurrenceOutcomeApplier.Apply(_ledger, effects);
            Assert.AreEqual(100f, _ledger.GetTradeResource(TradeResourceType.Wood).Amount);
        }

        [Test]
        public void Apply_TradeResourceLoss_SpendsFromLedger()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 20000f);
            _ledger.AddTradeResource(TradeResourceType.Wood, 200f);
            var effects = new[] { RandomOccurrenceOutcomeEffect.TradeResourceEffect(TradeResourceType.Wood, -50f, false) };
            RandomOccurrenceOutcomeApplier.Apply(_ledger, effects);
            Assert.AreEqual(150f, _ledger.GetTradeResource(TradeResourceType.Wood).Amount);
        }

        [Test]
        public void Apply_TradeResourcePercentageLoss_CalculatesCorrectly()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 20000f);
            _ledger.AddTradeResource(TradeResourceType.Wood, 500f);
            
            // Lose 20%
            var effects = new[] { RandomOccurrenceOutcomeEffect.TradeResourceEffect(TradeResourceType.Wood, -20f, true) };
            RandomOccurrenceOutcomeApplier.Apply(_ledger, effects);
            
            Assert.AreEqual(400f, _ledger.GetTradeResource(TradeResourceType.Wood).Amount);
        }

        [Test]
        public void Apply_NullEffectsArray_DoesNothing()
        {
            _ledger.AddGold(100);
            Assert.DoesNotThrow(() => RandomOccurrenceOutcomeApplier.Apply(_ledger, null));
            Assert.AreEqual(100f, _ledger.Gold);
        }
    }

    [TestFixture]
    public class WeightedOccurrenceSelectorTests
    {
        private RandomOccurrenceDefinition _eventA;
        private RandomOccurrenceDefinition _eventB;

        [SetUp]
        public void SetUp()
        {
            _eventA = ScriptableObject.CreateInstance<RandomOccurrenceDefinition>();
            _eventA.Weight = 10;
            _eventB = ScriptableObject.CreateInstance<RandomOccurrenceDefinition>();
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
            var result = WeightedOccurrenceSelector.Select(new List<RandomOccurrenceDefinition>(), rng);
            Assert.IsNull(result);
        }

        [Test]
        public void Select_NullPool_ReturnsNull()
        {
            var rng = new System.Random(1);
            var result = WeightedOccurrenceSelector.Select(null, rng);
            Assert.IsNull(result);
        }

        [Test]
        public void Select_SingleItemPool_ReturnsItem()
        {
            var rng = new System.Random(1);
            var result = WeightedOccurrenceSelector.Select(new List<RandomOccurrenceDefinition> { _eventA }, rng);
            Assert.AreEqual(_eventA, result);
        }

        [Test]
        public void Select_HigherWeightSelectedMoreOften()
        {
            var rng = new System.Random(42);
            var pool = new List<RandomOccurrenceDefinition> { _eventA, _eventB };

            int countA = 0;
            int countB = 0;

            for (int i = 0; i < 1000; i++)
            {
                var result = WeightedOccurrenceSelector.Select(pool, rng);
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
            var pool = new List<RandomOccurrenceDefinition> { _eventA, _eventB };
            var result = WeightedOccurrenceSelector.Select(pool, rng);
            Assert.IsNull(result);
        }
    }

    [TestFixture]
    public class RandomOccurrenceManagerTests
    {
        private EventBus _eventBus;
        private KingdomLedger _ledger;
        private FakeClock _clock;
        private RandomOccurrenceConfig _config;
        private RandomOccurrenceManager _manager;

        private RandomOccurrenceDefinition _eventA;
        private RandomOccurrenceDefinition _eventB;

        [SetUp]
        public void SetUp()
        {
            _eventBus = new EventBus();
            _ledger = new KingdomLedger(_eventBus);
            _clock = new FakeClock();
            
            _config = ScriptableObject.CreateInstance<RandomOccurrenceConfig>();
            _config.SpawnIntervalSeconds = 3600;
            _config.MaxPendingOccurrences = 5;

            _manager = new RandomOccurrenceManager(_ledger, _eventBus, _clock, _config);

            _eventA = ScriptableObject.CreateInstance<RandomOccurrenceDefinition>();
            _eventA.OccurrenceId = "OccurrenceA";
            _eventA.Weight = 100;
            _eventA.ChoiceA = new RandomOccurrenceChoice
            {
                Effects = new[] { RandomOccurrenceOutcomeEffect.GoldEffect(100f) }
            };
            _eventA.ChoiceB = new RandomOccurrenceChoice
            {
                Effects = new[] { RandomOccurrenceOutcomeEffect.GoldEffect(200f) }
            };

            _eventB = ScriptableObject.CreateInstance<RandomOccurrenceDefinition>();
            _eventB.OccurrenceId = "OccurrenceB";
            _eventB.Weight = 100;
            _eventB.ChoiceA = new RandomOccurrenceChoice
            {
                Effects = new[] { RandomOccurrenceOutcomeEffect.GoldEffect(-50f) }
            };
            _eventB.ChoiceB = new RandomOccurrenceChoice
            {
                Effects = new[] { RandomOccurrenceOutcomeEffect.GoldEffect(0f) }
            };

            _manager.InitializeOccurrencePool(new[] { _eventA, _eventB });
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_config);
            UnityEngine.Object.DestroyImmediate(_eventA);
            UnityEngine.Object.DestroyImmediate(_eventB);
        }

        [Test]
        public void ProcessSpawning_AddsOccurrenceAfterInterval()
        {
            Assert.AreEqual(0, _manager.PendingCount);
            _clock.Advance(TimeSpan.FromSeconds(3600));
            _manager.ProcessSpawning();
            Assert.AreEqual(1, _manager.PendingCount);
        }

        [Test]
        public void ProcessSpawning_CapsAtMaxPendingOccurrences()
        {
            _clock.Advance(TimeSpan.FromSeconds(3600 * 10)); // 10 hours
            _manager.ProcessSpawning();
            Assert.AreEqual(5, _manager.PendingCount); // Capped at MaxPendingOccurrences=5
        }

        [Test]
        public void ProcessSpawning_HandlesMultipleIntervalsOffline()
        {
            _clock.Advance(TimeSpan.FromSeconds(3600 * 3)); // 3 hours
            _manager.ProcessSpawning();
            Assert.AreEqual(3, _manager.PendingCount);
        }

        [Test]
        public void ResolveOccurrence_ChoiceIndex0_AppliesChoiceAEffects()
        {
            _manager.InitializeOccurrencePool(new[] { _eventA });
            _clock.Advance(TimeSpan.FromSeconds(3600));
            _manager.ProcessSpawning();

            bool result = _manager.ResolveOccurrence(0, 0);
            
            Assert.IsTrue(result);
            Assert.AreEqual(100f, _ledger.Gold);
        }

        [Test]
        public void ResolveOccurrence_ChoiceIndex1_AppliesChoiceBEffects()
        {
            _manager.InitializeOccurrencePool(new[] { _eventA });
            _clock.Advance(TimeSpan.FromSeconds(3600));
            _manager.ProcessSpawning();

            bool result = _manager.ResolveOccurrence(0, 1);
            
            Assert.IsTrue(result);
            Assert.AreEqual(200f, _ledger.Gold);
        }

        [Test]
        public void ResolveOccurrence_RemovesOccurrenceFromPending()
        {
            _clock.Advance(TimeSpan.FromSeconds(3600));
            _manager.ProcessSpawning();
            Assert.AreEqual(1, _manager.PendingCount);

            _manager.ResolveOccurrence(0, 0);
            
            Assert.AreEqual(0, _manager.PendingCount);
        }

        [Test]
        public void ResolveOccurrence_InvalidIndex_ReturnsFalse()
        {
            _clock.Advance(TimeSpan.FromSeconds(3600));
            _manager.ProcessSpawning();

            bool result = _manager.ResolveOccurrence(99, 0);
            
            Assert.IsFalse(result);
            Assert.AreEqual(1, _manager.PendingCount);
        }

        [Test]
        public void ResolveOccurrence_InvalidChoiceIndex_ReturnsFalse()
        {
            _clock.Advance(TimeSpan.FromSeconds(3600));
            _manager.ProcessSpawning();

            bool result = _manager.ResolveOccurrence(0, 99);
            
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
            
            _manager.ResolveOccurrence(0, 0);
            Assert.AreEqual(0, _manager.PendingCount);
        }
    }
}
