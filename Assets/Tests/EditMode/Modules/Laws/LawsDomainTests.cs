using System;
using System.Collections.Generic;
using NUnit.Framework;
using KingdomRuler.Core;
using KingdomRuler.Shared.Ledger;
using KingdomRuler.Shared.Services;
using KingdomRuler.Modules.Laws;
using KingdomRuler.Modules.Laws.Domain;
using UnityEngine;

namespace KingdomRuler.Tests.EditMode.Modules.Laws
{
    // Test helper: fake clock for deterministic time control
    public class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; } = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public void Advance(TimeSpan duration) => UtcNow += duration;
    }

    [TestFixture]
    public sealed class LevelingMathTests
    {
        [Test]
        public void DefaultPointsRequired_Level1_Returns100()
        {
            Assert.AreEqual(100f, LevelingMath.DefaultPointsRequired(1));
        }

        [Test]
        public void DefaultPointsRequired_Level2_Returns140()
        {
            Assert.AreEqual(140f, LevelingMath.DefaultPointsRequired(2));
        }

        [Test]
        public void DefaultPointsRequired_Level3_Returns180()
        {
            Assert.AreEqual(180f, LevelingMath.DefaultPointsRequired(3));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void DefaultPointsRequired_Level0OrNegative_ThrowsArgumentException(int level)
        {
            Assert.Throws<ArgumentException>(() => LevelingMath.DefaultPointsRequired(level));
        }

        [Test]
        public void PointsRequiredFromCurve_WithinBounds_ReturnsArrayValue()
        {
            var curve = new float[] { 50f, 150f, 300f };
            // Level 1 = index 0
            Assert.AreEqual(50f, LevelingMath.PointsRequiredFromCurve(1, curve));
            Assert.AreEqual(150f, LevelingMath.PointsRequiredFromCurve(2, curve));
            Assert.AreEqual(300f, LevelingMath.PointsRequiredFromCurve(3, curve));
        }

        [Test]
        public void PointsRequiredFromCurve_BeyondBounds_FallsBackToDefault()
        {
            var curve = new float[] { 50f };
            // Level 2 is beyond array, uses default: 140
            Assert.AreEqual(140f, LevelingMath.PointsRequiredFromCurve(2, curve));
        }

        [Test]
        public void PointsRequiredFromCurve_NullArray_FallsBackToDefault()
        {
            Assert.AreEqual(100f, LevelingMath.PointsRequiredFromCurve(1, null));
        }

        [Test]
        public void PointsRequiredFromCurve_Level0_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => LevelingMath.PointsRequiredFromCurve(0, new float[0]));
        }
    }

    [TestFixture]
    public sealed class CrystalBuyUpCalculatorTests
    {
        [TestCase(100f, 20f, 5)]
        [TestCase(95f, 20f, 5)]
        [TestCase(101f, 20f, 6)]
        [TestCase(1f, 20f, 1)]
        [TestCase(0f, 20f, 1)]
        [TestCase(-10f, 20f, 1)]
        public void CalculateCost_ReturnsExpectedCost(float remaining, float divisor, int expected)
        {
            Assert.AreEqual(expected, CrystalBuyUpCalculator.CalculateCost(remaining, divisor));
        }

        [Test]
        public void CalculateCost_DivisorZero_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => CrystalBuyUpCalculator.CalculateCost(100f, 0f));
        }
    }

    [TestFixture]
    public sealed class LawsManagerTests
    {
        private FakeClock _clock;
        private EventBus _eventBus;
        private KingdomLedger _ledger;
        private LawsConfig _config;
        private LawsManager _manager;
        private List<ScriptableObject> _createdAssets;

        [SetUp]
        public void SetUp()
        {
            _createdAssets = new List<ScriptableObject>();
            _clock = new FakeClock();
            _eventBus = new EventBus();
            _ledger = new KingdomLedger(_eventBus);
            _config = ScriptableObject.CreateInstance<LawsConfig>();
            _config.MaxHeldCards = 8;
            _config.CardReplenishTimeSeconds = 120;
            _config.CrystalCostPerRefill = 2;
            _createdAssets.Add(_config);
            
            _manager = new LawsManager(_ledger, _eventBus, _clock, _config);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in _createdAssets)
            {
                if (asset != null)
                {
                    UnityEngine.Object.DestroyImmediate(asset);
                }
            }
            _createdAssets.Clear();
        }

        private LawCardDefinition CreateTestCard(string id, LawCardEffect[] accept, LawCardEffect[] reject)
        {
            var card = ScriptableObject.CreateInstance<LawCardDefinition>();
            card.CardId = id;
            card.AcceptEffects = accept;
            card.RejectEffects = reject;
            _createdAssets.Add(card);
            return card;
        }

        [Test]
        public void ResolveCard_Accept_AppliesAcceptEffects()
        {
            var acceptEffect = new LawCardEffect { Characteristic = CharacteristicType.Army, Points = 10f };
            var card = CreateTestCard("test_card", new[] { acceptEffect }, null);
            _manager.InitializeCardPool(new[] { card });
            
            _manager.ProcessReplenishment(); // Start process
            _clock.Advance(TimeSpan.FromSeconds(120));
            _manager.ProcessReplenishment(); 

            float initialMilitary = _ledger.GetCharacteristic(CharacteristicType.Army).PointsIntoCurrentLevel;
            
            bool resolved = _manager.ResolveCard(0, true);
            Assert.IsTrue(resolved);
            Assert.AreEqual(initialMilitary + 10f, _ledger.GetCharacteristic(CharacteristicType.Army).PointsIntoCurrentLevel);
        }

        [Test]
        public void ResolveCard_Reject_AppliesRejectEffects()
        {
            var rejectEffect = new LawCardEffect { Characteristic = CharacteristicType.Infrastructure, Points = 5f };
            var card = CreateTestCard("test_card", null, new[] { rejectEffect });
            _manager.InitializeCardPool(new[] { card });
            
            _clock.Advance(TimeSpan.FromSeconds(120));
            _manager.ProcessReplenishment(); 

            float initialEconomy = _ledger.GetCharacteristic(CharacteristicType.Infrastructure).PointsIntoCurrentLevel;
            
            bool resolved = _manager.ResolveCard(0, false);
            Assert.IsTrue(resolved);
            Assert.AreEqual(initialEconomy + 5f, _ledger.GetCharacteristic(CharacteristicType.Infrastructure).PointsIntoCurrentLevel);
        }

        [Test]
        public void ResolveCard_RemovesCardFromHeldList()
        {
            var card = CreateTestCard("test_card", null, null);
            _manager.InitializeCardPool(new[] { card });
            _clock.Advance(TimeSpan.FromSeconds(120));
            _manager.ProcessReplenishment(); 

            Assert.AreEqual(1, _manager.HeldCards.Count);
            _manager.ResolveCard(0, true);
            Assert.AreEqual(0, _manager.HeldCards.Count);
        }

        [Test]
        public void ResolveCard_InvalidIndex_ReturnsFalse()
        {
            Assert.IsFalse(_manager.ResolveCard(0, true));
            Assert.IsFalse(_manager.ResolveCard(-1, true));
        }

        [Test]
        public void ResolveCard_StartsReplenishmentTimer()
        {
            var card = CreateTestCard("test_card", null, null);
            _manager.InitializeCardPool(new[] { card });
            
            // Fill completely
            for(int i = 0; i < 8; i++) 
            {
                _clock.Advance(TimeSpan.FromSeconds(120));
                _manager.ProcessReplenishment();
            }
            Assert.AreEqual(8, _manager.HeldCards.Count);
            Assert.AreEqual(0, _manager.CardsReplenishing);

            // Resolve one, should start replenishing
            _manager.ResolveCard(0, true);
            Assert.AreEqual(7, _manager.HeldCards.Count);
            Assert.AreEqual(1, _manager.CardsReplenishing);
            Assert.AreEqual(120f, _manager.GetSecondsUntilNextCard());
        }

        [Test]
        public void ProcessReplenishment_AddsCardAfterEnoughTime()
        {
            var card = CreateTestCard("test_card", null, null);
            _manager.InitializeCardPool(new[] { card });
            
            Assert.AreEqual(0, _manager.HeldCards.Count);
            _clock.Advance(TimeSpan.FromSeconds(119));
            _manager.ProcessReplenishment();
            Assert.AreEqual(0, _manager.HeldCards.Count);

            _clock.Advance(TimeSpan.FromSeconds(1));
            _manager.ProcessReplenishment();
            Assert.AreEqual(1, _manager.HeldCards.Count);
        }

        [Test]
        public void ProcessReplenishment_HandlesMultipleCardsWorthOfElapsedTime()
        {
            var card = CreateTestCard("test_card", null, null);
            _manager.InitializeCardPool(new[] { card });
            
            _clock.Advance(TimeSpan.FromSeconds(360)); // 3 cards worth
            _manager.ProcessReplenishment();
            Assert.AreEqual(3, _manager.HeldCards.Count);
        }

        [Test]
        public void ProcessReplenishment_CapsAtMaxHeldCards()
        {
            var card = CreateTestCard("test_card", null, null);
            _manager.InitializeCardPool(new[] { card });
            
            _clock.Advance(TimeSpan.FromSeconds(12000)); // Plenty of time
            _manager.ProcessReplenishment();
            Assert.AreEqual(8, _manager.HeldCards.Count); // Max is 8
        }

        [Test]
        public void ProcessReplenishment_NoCardsReplenishing_DoesNothing()
        {
            var card = CreateTestCard("test_card", null, null);
            _manager.InitializeCardPool(new[] { card });
            
            // Fill completely
            _clock.Advance(TimeSpan.FromSeconds(1200));
            _manager.ProcessReplenishment();
            Assert.AreEqual(8, _manager.HeldCards.Count);
            
            _clock.Advance(TimeSpan.FromSeconds(120));
            _manager.ProcessReplenishment();
            Assert.AreEqual(8, _manager.HeldCards.Count);
        }

        [Test]
        public void RefillWithCrystals_FillsToCapAndSpendsCrystals()
        {
            var card = CreateTestCard("test_card", null, null);
            _manager.InitializeCardPool(new[] { card });
            
            // Cost = 8 missing × 2 crystals/card = 16
            _ledger.AddCrystals(20);
            
            Assert.AreEqual(0, _manager.HeldCards.Count);
            
            bool result = _manager.RefillWithCrystals();
            Assert.IsTrue(result);
            Assert.AreEqual(8, _manager.HeldCards.Count);
            Assert.AreEqual(4, _ledger.Crystals); // 20 - 16 = 4
        }

        [Test]
        public void RefillWithCrystals_FailsIfNotEnoughCrystals()
        {
            var card = CreateTestCard("test_card", null, null);
            _manager.InitializeCardPool(new[] { card });
            
            _ledger.AddCrystals(1); 
            
            bool result = _manager.RefillWithCrystals();
            Assert.IsFalse(result);
            Assert.AreEqual(0, _manager.HeldCards.Count);
            Assert.AreEqual(1, _ledger.Crystals);
        }

        [Test]
        public void RefillWithCrystals_AlreadyFull_ReturnsFalse()
        {
            var card = CreateTestCard("test_card", null, null);
            _manager.InitializeCardPool(new[] { card });
            
            _clock.Advance(TimeSpan.FromSeconds(1200));
            _manager.ProcessReplenishment();
            
            _ledger.AddCrystals(10);
            
            bool result = _manager.RefillWithCrystals();
            Assert.IsFalse(result);
            Assert.AreEqual(10, _ledger.Crystals);
        }

        [Test]
        public void BuyUpCharacteristic_SpendsCrystalsAndLevelsUp()
        {
            _ledger.AddCrystals(100);
            int initialLevel = _ledger.GetCharacteristic(CharacteristicType.Army).Level;
            
            bool result = _manager.BuyUpCharacteristic(CharacteristicType.Army);
            
            Assert.IsTrue(result);
            Assert.AreEqual(initialLevel + 1, _ledger.GetCharacteristic(CharacteristicType.Army).Level);
            Assert.Less(_ledger.Crystals, 100); // verify spent
        }

        [Test]
        public void BuyUpCharacteristic_FailsIfNotEnoughCrystals()
        {
            _ledger.AddCrystals(0);
            int initialLevel = _ledger.GetCharacteristic(CharacteristicType.Army).Level;
            
            bool result = _manager.BuyUpCharacteristic(CharacteristicType.Army);
            
            Assert.IsFalse(result);
            Assert.AreEqual(initialLevel, _ledger.GetCharacteristic(CharacteristicType.Army).Level);
        }

        [Test]
        public void GetSecondsUntilNextCard_ReturnsCorrectRemainingTime()
        {
            var card = CreateTestCard("test_card", null, null);
            _manager.InitializeCardPool(new[] { card });
            
            _clock.Advance(TimeSpan.FromSeconds(20));
            _manager.ProcessReplenishment();
            
            Assert.AreEqual(100f, _manager.GetSecondsUntilNextCard(), 0.01f);
        }

        [Test]
        public void GetSecondsUntilNextCard_ReturnsZeroWhenNoReplenishment()
        {
            var card = CreateTestCard("test_card", null, null);
            _manager.InitializeCardPool(new[] { card });
            
            _clock.Advance(TimeSpan.FromSeconds(1200));
            _manager.ProcessReplenishment(); // Full capacity
            
            Assert.AreEqual(0f, _manager.GetSecondsUntilNextCard());
        }
    }
}
