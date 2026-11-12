using System;
using System.Text.RegularExpressions;
using KingdomRuler.Systems.Events;
using KingdomRuler.Systems.Ledger;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KingdomRuler.Tests.EditMode.Systems
{
    public class KingdomLedgerTests
    {
        private EventBus _eventBus;
        private KingdomLedger _ledger;

        // Thresholds come from the GDD §6 curve (CharacteristicLevelingCurve):
        // level 1 = 100, level 2 = 140, level 3 = 180.
        [SetUp]
        public void SetUp()
        {
            _eventBus = new EventBus();
            _ledger = new KingdomLedger(_eventBus);
        }

        #region Gold Tests

        [Test]
        public void AddGold_IncreasesGold()
        {
            _ledger.AddGold(100);
            Assert.AreEqual(100, _ledger.Gold);
        }

        [Test]
        public void AddGold_PublishesGoldChangedEvent_WithCorrectNewAmountAndDelta()
        {
            GoldChangedEvent? receivedEvent = null;
            _eventBus.Subscribe<GoldChangedEvent>(e => receivedEvent = e);

            _ledger.AddGold(50);

            Assert.IsNotNull(receivedEvent);
            Assert.AreEqual(50, receivedEvent.Value.NewAmount);
            Assert.AreEqual(50, receivedEvent.Value.Delta);
        }

        [Test]
        public void SpendGold_DecreasesGold_AndReturnsTrue()
        {
            _ledger.AddGold(100);
            bool result = _ledger.SpendGold(40);

            Assert.IsTrue(result);
            Assert.AreEqual(60, _ledger.Gold);
        }

        [Test]
        public void SpendGold_ReturnsFalse_AndDoesNotChangeGold_WhenInsufficient()
        {
            _ledger.AddGold(20);
            bool result = _ledger.SpendGold(50);

            Assert.IsFalse(result);
            Assert.AreEqual(20, _ledger.Gold);
        }

        [Test]
        public void SpendGold_PublishesGoldChangedEvent_WithNegativeDelta()
        {
            _ledger.AddGold(100);

            GoldChangedEvent? receivedEvent = null;
            _eventBus.Subscribe<GoldChangedEvent>(e => receivedEvent = e);

            _ledger.SpendGold(30);

            Assert.IsNotNull(receivedEvent);
            Assert.AreEqual(70, receivedEvent.Value.NewAmount);
            Assert.AreEqual(-30, receivedEvent.Value.Delta);
        }

        [Test]
        public void SetGold_SetsGoldDirectly()
        {
            _ledger.SetGold(500);
            Assert.AreEqual(500, _ledger.Gold);
        }

        [Test]
        public void AddGold_WithZero_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _ledger.AddGold(0));
            Assert.AreEqual(0, _ledger.Gold);
        }

        [Test]
        public void AddGold_WithNegative_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => _ledger.AddGold(-10));
        }

        [Test]
        public void SpendGold_WithNegative_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => _ledger.SpendGold(-10));
        }

        [Test]
        public void SetGold_WithNegative_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => _ledger.SetGold(-10));
        }

        // ── The ceiling (KingdomLedger.MaxGold) ──────────────────────────────────

        [Test]
        public void AddGold_ReturnsWhatWasAdded()
        {
            Assert.AreEqual(100, _ledger.AddGold(100));
        }

        [Test]
        public void AddGold_UpToTheCeilingExactly_AddsAll()
        {
            _ledger.SetGold(KingdomLedger.MaxGold - 25);

            Assert.AreEqual(25, _ledger.AddGold(25));
            Assert.AreEqual(KingdomLedger.MaxGold, _ledger.Gold);
        }

        [Test]
        public void AddGold_PastTheCeiling_StopsAtIt_AndReportsOnlyWhatFitted()
        {
            _ledger.SetGold(KingdomLedger.MaxGold - 10);
            GoldChangedEvent? receivedEvent = null;
            _eventBus.Subscribe<GoldChangedEvent>(e => receivedEvent = e);

            long added = _ledger.AddGold(25);

            Assert.AreEqual(10, added);
            Assert.AreEqual(KingdomLedger.MaxGold, _ledger.Gold);
            Assert.AreEqual(KingdomLedger.MaxGold, receivedEvent.Value.NewAmount);
            Assert.AreEqual(10, receivedEvent.Value.Delta, "The event carries the real change, not the request.");
        }

        [Test]
        public void AddGold_AtTheCeiling_AddsNothing_AndPublishesNothing()
        {
            _ledger.SetGold(KingdomLedger.MaxGold);
            int events = 0;
            _eventBus.Subscribe<GoldChangedEvent>(_ => events++);

            Assert.AreEqual(0, _ledger.AddGold(1));
            Assert.AreEqual(KingdomLedger.MaxGold, _ledger.Gold);
            Assert.AreEqual(0, events);
        }

        [Test]
        public void AddGold_OfAnyAmount_NeverOverflows()
        {
            _ledger.AddGold(5);

            Assert.AreEqual(KingdomLedger.MaxGold - 5, _ledger.AddGold(long.MaxValue));
            Assert.AreEqual(KingdomLedger.MaxGold, _ledger.Gold);
        }

        [Test]
        public void AddGold_WithZero_PublishesNothing()
        {
            int events = 0;
            _eventBus.Subscribe<GoldChangedEvent>(_ => events++);

            Assert.AreEqual(0, _ledger.AddGold(0));
            Assert.AreEqual(0, events);
        }

        [Test]
        public void SetGold_AtTheCeiling_IsAllowed_AndAboveItThrows()
        {
            _ledger.SetGold(KingdomLedger.MaxGold);
            Assert.AreEqual(KingdomLedger.MaxGold, _ledger.Gold);

            Assert.Throws<ArgumentException>(() => _ledger.SetGold(KingdomLedger.MaxGold + 1));
            Assert.AreEqual(KingdomLedger.MaxGold, _ledger.Gold, "A refused set changes nothing.");
        }

        #endregion

        #region Crystal Tests

        [Test]
        public void AddCrystals_IncreasesCrystals()
        {
            _ledger.AddCrystals(10);
            Assert.AreEqual(10, _ledger.Crystals);
        }

        [Test]
        public void AddCrystals_PublishesCrystalsChangedEvent()
        {
            CrystalsChangedEvent? receivedEvent = null;
            _eventBus.Subscribe<CrystalsChangedEvent>(e => receivedEvent = e);

            _ledger.AddCrystals(5);

            Assert.IsNotNull(receivedEvent);
            Assert.AreEqual(5, receivedEvent.Value.NewAmount);
            Assert.AreEqual(5, receivedEvent.Value.Delta);
        }

        [Test]
        public void SpendCrystals_DecreasesCrystals_AndReturnsTrue()
        {
            _ledger.AddCrystals(10);
            bool result = _ledger.SpendCrystals(4);

            Assert.IsTrue(result);
            Assert.AreEqual(6, _ledger.Crystals);
        }

        [Test]
        public void SpendCrystals_ReturnsFalse_WhenInsufficient()
        {
            _ledger.AddCrystals(5);
            bool result = _ledger.SpendCrystals(10);

            Assert.IsFalse(result);
            Assert.AreEqual(5, _ledger.Crystals);
        }

        [Test]
        public void AddCrystals_WithNegative_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => _ledger.AddCrystals(-5));
        }

        [Test]
        public void SpendCrystals_WithNegative_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => _ledger.SpendCrystals(-5));
        }

        #endregion

        #region Trade Resource Tests

        [Test]
        public void AddTradeResource_IncreasesAmount()
        {
            var added = _ledger.AddTradeResource(TradeResourceType.Wood, 50);
            Assert.AreEqual(50, added);
            Assert.AreEqual(50, _ledger.GetTradeResource(TradeResourceType.Wood).Amount);
        }

        [Test]
        public void AddTradeResource_CapsAtWarehouseCapacity_ReturnsActualAdded()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 100);
            var added = _ledger.AddTradeResource(TradeResourceType.Wood, 150);

            Assert.AreEqual(100, added);
            Assert.AreEqual(100, _ledger.GetTradeResource(TradeResourceType.Wood).Amount);
        }

        [Test]
        public void AddTradeResource_AlreadyAtCapacity_ReturnsZero()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 100);
            _ledger.AddTradeResource(TradeResourceType.Wood, 100);
            
            var added = _ledger.AddTradeResource(TradeResourceType.Wood, 50);

            Assert.AreEqual(0, added);
            Assert.AreEqual(100, _ledger.GetTradeResource(TradeResourceType.Wood).Amount);
        }

        [Test]
        public void SpendTradeResource_DecreasesAndReturnsTrue()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 100);
            _ledger.AddTradeResource(TradeResourceType.Wood, 100);

            bool result = _ledger.SpendTradeResource(TradeResourceType.Wood, 40);

            Assert.IsTrue(result);
            Assert.AreEqual(60, _ledger.GetTradeResource(TradeResourceType.Wood).Amount);
        }

        [Test]
        public void SpendTradeResource_ReturnsFalse_WhenInsufficient()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 100);
            _ledger.AddTradeResource(TradeResourceType.Wood, 30);

            bool result = _ledger.SpendTradeResource(TradeResourceType.Wood, 40);

            Assert.IsFalse(result);
            Assert.AreEqual(30, _ledger.GetTradeResource(TradeResourceType.Wood).Amount);
        }

        [Test]
        public void AddTradeResource_PublishesResourceChanged()
        {
            TradeResourceChangedEvent? receivedEvent = null;
            _eventBus.Subscribe<TradeResourceChangedEvent>(e =>
            {
                if (e.ResourceType == TradeResourceType.Wood)
                    receivedEvent = e;
            });

            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 100);
            _ledger.AddTradeResource(TradeResourceType.Wood, 50);

            Assert.IsNotNull(receivedEvent);
            Assert.AreEqual(TradeResourceType.Wood, receivedEvent.Value.ResourceType);
            Assert.AreEqual(50, receivedEvent.Value.NewAmount);
            Assert.AreEqual(50, receivedEvent.Value.Delta);
        }

        [Test]
        public void HasTradeResource_ReturnsTrue_WhenSufficient()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 100);
            _ledger.AddTradeResource(TradeResourceType.Wood, 50);

            Assert.IsTrue(_ledger.HasTradeResource(TradeResourceType.Wood, 30));
            Assert.IsTrue(_ledger.HasTradeResource(TradeResourceType.Wood, 50));
        }

        [Test]
        public void HasTradeResource_ReturnsFalse_WhenInsufficient()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 100);
            _ledger.AddTradeResource(TradeResourceType.Wood, 50);

            Assert.IsFalse(_ledger.HasTradeResource(TradeResourceType.Wood, 60));
        }

        [Test]
        public void SetWarehouseCapacity_ChangesCapacityAndTheFillTime()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 200);

            var state = _ledger.GetTradeResource(TradeResourceType.Wood);
            Assert.AreEqual(200, state.Capacity);
            Assert.AreEqual(TimeSpan.FromDays(1).Ticks / 200, state.TimePerUnit.Ticks,
                "An empty warehouse refills in one day whatever its size (GDD §7).");
        }

        [Test]
        public void SetWarehouseCapacity_AboveTheCeiling_IsClampedAndLogged()
        {
            LogAssert.Expect(LogType.Error, new Regex("ceiling"));
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, KingdomLedger.MaxWarehouseCapacity + 1);

            Assert.AreEqual(KingdomLedger.MaxWarehouseCapacity, _ledger.GetTradeResource(TradeResourceType.Wood).Capacity);
        }

        [Test]
        public void AddTradeResource_FillingToCapacity_LandsExactlyOnCapacity()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 100);
            _ledger.AddTradeResource(TradeResourceType.Wood, 87);

            Assert.AreEqual(13, _ledger.AddTradeResource(TradeResourceType.Wood, 30));
            Assert.AreEqual(100, _ledger.GetTradeResource(TradeResourceType.Wood).Amount);
        }

        [Test]
        public void AddTradeResource_WithNegative_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => _ledger.AddTradeResource(TradeResourceType.Wood, -10));
        }

        [Test]
        public void SpendTradeResource_WithNegative_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => _ledger.SpendTradeResource(TradeResourceType.Wood, -10));
        }

        #endregion

        #region Characteristic Tests

        [Test]
        public void AddCharacteristicPoints_IncreasesPointsIntoCurrentLevel()
        {
            _ledger.AddCharacteristicPoints(CharacteristicType.Medicine, 40);

            var state = _ledger.GetCharacteristic(CharacteristicType.Medicine);
            Assert.AreEqual(1, state.Level);
            Assert.AreEqual(40, state.PointsIntoCurrentLevel);
        }

        [Test]
        public void AddCharacteristicPoints_TriggersLevelUp_WhenThresholdReached()
        {
            _ledger.AddCharacteristicPoints(CharacteristicType.Medicine, 100);

            var state = _ledger.GetCharacteristic(CharacteristicType.Medicine);
            Assert.AreEqual(2, state.Level);
            Assert.AreEqual(0, state.PointsIntoCurrentLevel);
        }

        [Test]
        public void AddCharacteristicPoints_CarriesOverExcessPoints_AfterLevelUp()
        {
            _ledger.AddCharacteristicPoints(CharacteristicType.Medicine, 130);

            var state = _ledger.GetCharacteristic(CharacteristicType.Medicine);
            Assert.AreEqual(2, state.Level);
            Assert.AreEqual(30, state.PointsIntoCurrentLevel);
        }

        [Test]
        public void AddCharacteristicPoints_PublishesCharacteristicLeveledUp_OnLevelUp()
        {
            CharacteristicLeveledUpEvent? receivedEvent = null;
            _eventBus.Subscribe<CharacteristicLeveledUpEvent>(e => receivedEvent = e);

            _ledger.AddCharacteristicPoints(CharacteristicType.Medicine, 110);

            Assert.IsNotNull(receivedEvent);
            Assert.AreEqual(CharacteristicType.Medicine, receivedEvent.Value.CharacteristicType);
            Assert.AreEqual(2, receivedEvent.Value.NewLevel);
        }

        [Test]
        public void AddCharacteristicPoints_MultipleLevelUpsInOneCall()
        {
            // 250 = 100 (clears level 1) + 140 (clears level 2), leaving 10 into level 3.
            _ledger.AddCharacteristicPoints(CharacteristicType.Medicine, 250);

            var state = _ledger.GetCharacteristic(CharacteristicType.Medicine);
            Assert.AreEqual(3, state.Level);
            Assert.AreEqual(10, state.PointsIntoCurrentLevel);
        }

        /// <summary>An award far larger than any level must level up through it, not overflow.</summary>
        [Test]
        public void AddCharacteristicPoints_HugeAward_DoesNotOverflow()
        {
            _ledger.AddCharacteristicPoints(CharacteristicType.Medicine, 400);
            _ledger.AddCharacteristicPoints(CharacteristicType.Medicine, int.MaxValue);

            var state = _ledger.GetCharacteristic(CharacteristicType.Medicine);
            Assert.Greater(state.Level, 1);
            Assert.GreaterOrEqual(state.PointsIntoCurrentLevel, 0);
            Assert.Less(state.PointsIntoCurrentLevel, _ledger.PointsRequiredForLevel(state.Level));
        }

        [Test]
        public void ReduceCharacteristicPoints_DecreasesPoints()
        {
            _ledger.AddCharacteristicPoints(CharacteristicType.Medicine, 60);
            _ledger.ReduceCharacteristicPoints(CharacteristicType.Medicine, 20);

            var state = _ledger.GetCharacteristic(CharacteristicType.Medicine);
            Assert.AreEqual(1, state.Level);
            Assert.AreEqual(40, state.PointsIntoCurrentLevel);
        }

        /// <summary>
        /// GDD §6: the level floor is permanent. A reduction bigger than the progress
        /// made into the current level must stop at zero, never roll the level back.
        /// </summary>
        [Test]
        public void ReduceCharacteristicPoints_LargeReduction_NeverDropsTheLevel()
        {
            // 150 = 100 (clears level 1) + 50 into level 2.
            _ledger.AddCharacteristicPoints(CharacteristicType.Medicine, 150);

            // Far more than the 50 points of progress held at level 2.
            _ledger.ReduceCharacteristicPoints(CharacteristicType.Medicine, 200);

            var state = _ledger.GetCharacteristic(CharacteristicType.Medicine);
            Assert.AreEqual(2, state.Level, "The level reached must be permanent.");
            Assert.AreEqual(0, state.PointsIntoCurrentLevel, "Progress clamps at zero.");
        }

        [Test]
        public void ReduceCharacteristicPoints_AtZeroProgress_ClampsAndStaysAtLevelOne()
        {
            _ledger.ReduceCharacteristicPoints(CharacteristicType.Medicine, 50);

            var state = _ledger.GetCharacteristic(CharacteristicType.Medicine);
            Assert.AreEqual(1, state.Level);
            Assert.AreEqual(0, state.PointsIntoCurrentLevel);
        }

        [Test]
        public void ReduceCharacteristicPoints_NeverPublishesALevelChange()
        {
            _ledger.AddCharacteristicPoints(CharacteristicType.Medicine, 300);

            int published = 0;
            _eventBus.Subscribe<CharacteristicLeveledUpEvent>(_ => published++);
            _ledger.ReduceCharacteristicPoints(CharacteristicType.Medicine, 10000);

            Assert.AreEqual(0, published, "Reductions never change the level, so nothing to announce.");
        }

        [Test]
        public void MeetsCharacteristicLevel_ReturnsTrue_WhenAtOrAboveRequired()
        {
            _ledger.AddCharacteristicPoints(CharacteristicType.Medicine, 240); // 100 + 140 → level 3

            Assert.IsTrue(_ledger.MeetsCharacteristicLevel(CharacteristicType.Medicine, 1));
            Assert.IsTrue(_ledger.MeetsCharacteristicLevel(CharacteristicType.Medicine, 2));
            Assert.IsTrue(_ledger.MeetsCharacteristicLevel(CharacteristicType.Medicine, 3));
        }

        [Test]
        public void MeetsCharacteristicLevel_ReturnsFalse_WhenBelowRequired()
        {
            Assert.IsFalse(_ledger.MeetsCharacteristicLevel(CharacteristicType.Medicine, 2));
        }

        [Test]
        public void LevelReached_IsPermanent_AcrossGainAndLoss()
        {
            _ledger.AddCharacteristicPoints(CharacteristicType.Medicine, 100); // → level 2
            Assert.AreEqual(2, _ledger.GetCharacteristic(CharacteristicType.Medicine).Level);

            _ledger.ReduceCharacteristicPoints(CharacteristicType.Medicine, 100);

            var state = _ledger.GetCharacteristic(CharacteristicType.Medicine);
            Assert.AreEqual(2, state.Level, "Level 2 was reached, so it is permanent.");
            Assert.AreEqual(0, state.PointsIntoCurrentLevel);
        }

        /// <summary>
        /// EventBus.Publish is synchronous, so a subscriber runs inside the mutating call.
        /// It must never observe a partially-applied state — the level bumped but the
        /// carried-over points not yet subtracted. Publishing from inside the level-up
        /// loop allowed exactly that.
        /// </summary>
        [Test]
        public void AddCharacteristicPoints_SubscriberAlwaysObservesFullySettledState()
        {
            // 250 = 100 (level 1) + 140 (level 2), leaving 10 into level 3.
            var observed = new System.Collections.Generic.List<(int level, int points)>();
            _eventBus.Subscribe<CharacteristicLeveledUpEvent>(_ =>
            {
                var s = _ledger.GetCharacteristic(CharacteristicType.Medicine);
                observed.Add((s.Level, s.PointsIntoCurrentLevel));
            });

            _ledger.AddCharacteristicPoints(CharacteristicType.Medicine, 250);

            Assert.AreEqual(2, observed.Count, "Two level-ups should be announced.");
            foreach (var (level, points) in observed)
            {
                Assert.AreEqual(3, level, "State must already be final when a subscriber runs.");
                Assert.AreEqual(10f, points, 0.001f,
                    "Carried-over points must already be settled when a subscriber runs.");
            }
        }

        [Test]
        public void AddCharacteristicPoints_AnnouncesEveryLevelCrossed_InOrder()
        {
            var levels = new System.Collections.Generic.List<int>();
            _eventBus.Subscribe<CharacteristicLeveledUpEvent>(e => levels.Add(e.NewLevel));

            _ledger.AddCharacteristicPoints(CharacteristicType.Medicine, 250);

            CollectionAssert.AreEqual(new[] { 2, 3 }, levels);
        }

        #endregion

        #region Offline Accrual Tests

        [Test]
        public void AccruePassiveResourceRegen_AddsResourcesBasedOnElapsedTime()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 86400);
            
            var now = DateTime.UtcNow;
            _ledger.ResetRegenBaseline(now.AddSeconds(-10));

            _ledger.AccruePassiveResourceRegen(now);

            // Regen rate is Capacity / 86400 = 1/sec
            // 10 seconds elapsed = 10 resources
            Assert.AreEqual(10, _ledger.GetTradeResource(TradeResourceType.Wood).Amount);
        }

        [Test]
        public void AccruePassiveResourceRegen_CapsAtCapacity()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 100);
            
            var now = DateTime.UtcNow;
            _ledger.ResetRegenBaseline(now.AddDays(-2));

            _ledger.AccruePassiveResourceRegen(now);

            Assert.AreEqual(100, _ledger.GetTradeResource(TradeResourceType.Wood).Amount);
        }

        [Test]
        public void AccruePassiveResourceRegen_WithNoElapsedTime_AddsNothing()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 100);
            
            var now = DateTime.UtcNow;
            _ledger.ResetRegenBaseline(now);

            _ledger.AccruePassiveResourceRegen(now);

            Assert.AreEqual(0, _ledger.GetTradeResource(TradeResourceType.Wood).Amount);
        }

        #endregion

        #region Snapshot Tests

        [Test]
        public void TakeSnapshot_ReturnsCorrectGoldAndCrystals()
        {
            _ledger.AddGold(150);
            _ledger.AddCrystals(20);

            var snapshot = _ledger.TakeSnapshot();

            Assert.AreEqual(150, snapshot.Gold);
            Assert.AreEqual(20, snapshot.Crystals);
        }

        [Test]
        public void TakeSnapshot_ReturnsCorrectTradeResourceAmounts()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 100);
            _ledger.AddTradeResource(TradeResourceType.Wood, 45);

            var snapshot = _ledger.TakeSnapshot();

            Assert.IsTrue(snapshot.TradeResourceAmounts.ContainsKey(TradeResourceType.Wood));
            Assert.AreEqual(45, snapshot.TradeResourceAmounts[TradeResourceType.Wood]);
        }

        [Test]
        public void TakeSnapshot_ReturnsCorrectCharacteristicLevels()
        {
            _ledger.AddCharacteristicPoints(CharacteristicType.Science, 100); // level 2

            var snapshot = _ledger.TakeSnapshot();

            Assert.IsTrue(snapshot.CharacteristicLevels.ContainsKey(CharacteristicType.Science));
            Assert.AreEqual(2, snapshot.CharacteristicLevels[CharacteristicType.Science]);
        }

        [Test]
        public void Snapshot_IsImmutable_MutatingLedgerAfterSnapshotDoesNotChangeSnapshot()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 100);
            _ledger.AddTradeResource(TradeResourceType.Wood, 40);
            _ledger.AddGold(100);

            var snapshot = _ledger.TakeSnapshot();

            _ledger.AddTradeResource(TradeResourceType.Wood, 10);
            _ledger.AddGold(50);

            Assert.AreEqual(40, snapshot.TradeResourceAmounts[TradeResourceType.Wood]);
            Assert.AreEqual(100, snapshot.Gold);
        }

        #endregion
    }
}
