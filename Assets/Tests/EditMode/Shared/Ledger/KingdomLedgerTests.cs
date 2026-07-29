using System;
using KingdomRuler.Core;
using KingdomRuler.Shared.Ledger;
using NUnit.Framework;

namespace KingdomRuler.Tests.EditMode.Shared
{
    public class KingdomLedgerTests
    {
        private EventBus _eventBus;
        private KingdomLedger _ledger;

        private float PointsRequired(int level) => 100f;

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
            GoldChanged? receivedEvent = null;
            _eventBus.Subscribe<GoldChanged>(e => receivedEvent = e);

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

            GoldChanged? receivedEvent = null;
            _eventBus.Subscribe<GoldChanged>(e => receivedEvent = e);

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
            CrystalsChanged? receivedEvent = null;
            _eventBus.Subscribe<CrystalsChanged>(e => receivedEvent = e);

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
            var added = _ledger.AddTradeResource(TradeResourceType.Wood, 50f);
            Assert.AreEqual(50f, added);
            Assert.AreEqual(50f, _ledger.GetTradeResource(TradeResourceType.Wood).Amount);
        }

        [Test]
        public void AddTradeResource_CapsAtWarehouseCapacity_ReturnsActualAdded()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 100f);
            var added = _ledger.AddTradeResource(TradeResourceType.Wood, 150f);

            Assert.AreEqual(100f, added);
            Assert.AreEqual(100f, _ledger.GetTradeResource(TradeResourceType.Wood).Amount);
        }

        [Test]
        public void AddTradeResource_AlreadyAtCapacity_ReturnsZero()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 100f);
            _ledger.AddTradeResource(TradeResourceType.Wood, 100f);
            
            var added = _ledger.AddTradeResource(TradeResourceType.Wood, 50f);

            Assert.AreEqual(0f, added);
            Assert.AreEqual(100f, _ledger.GetTradeResource(TradeResourceType.Wood).Amount);
        }

        [Test]
        public void SpendTradeResource_DecreasesAndReturnsTrue()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 100f);
            _ledger.AddTradeResource(TradeResourceType.Wood, 100f);

            bool result = _ledger.SpendTradeResource(TradeResourceType.Wood, 40f);

            Assert.IsTrue(result);
            Assert.AreEqual(60f, _ledger.GetTradeResource(TradeResourceType.Wood).Amount);
        }

        [Test]
        public void SpendTradeResource_ReturnsFalse_WhenInsufficient()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 100f);
            _ledger.AddTradeResource(TradeResourceType.Wood, 30f);

            bool result = _ledger.SpendTradeResource(TradeResourceType.Wood, 40f);

            Assert.IsFalse(result);
            Assert.AreEqual(30f, _ledger.GetTradeResource(TradeResourceType.Wood).Amount);
        }

        [Test]
        public void AddTradeResource_PublishesResourceChanged()
        {
            ResourceChanged? receivedEvent = null;
            _eventBus.Subscribe<ResourceChanged>(e =>
            {
                if (e.ResourceType == TradeResourceType.Wood)
                    receivedEvent = e;
            });

            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 100f);
            _ledger.AddTradeResource(TradeResourceType.Wood, 50f);

            Assert.IsNotNull(receivedEvent);
            Assert.AreEqual(TradeResourceType.Wood, receivedEvent.Value.ResourceType);
            Assert.AreEqual(50f, receivedEvent.Value.NewAmount);
            Assert.AreEqual(50f, receivedEvent.Value.Delta);
        }

        [Test]
        public void HasTradeResource_ReturnsTrue_WhenSufficient()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 100f);
            _ledger.AddTradeResource(TradeResourceType.Wood, 50f);

            Assert.IsTrue(_ledger.HasTradeResource(TradeResourceType.Wood, 30f));
            Assert.IsTrue(_ledger.HasTradeResource(TradeResourceType.Wood, 50f));
        }

        [Test]
        public void HasTradeResource_ReturnsFalse_WhenInsufficient()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 100f);
            _ledger.AddTradeResource(TradeResourceType.Wood, 50f);

            Assert.IsFalse(_ledger.HasTradeResource(TradeResourceType.Wood, 60f));
        }

        [Test]
        public void SetWarehouseCapacity_ChangesCapacityAndRegenRate()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 200f);

            var state = _ledger.GetTradeResource(TradeResourceType.Wood);
            Assert.AreEqual(200f, state.Capacity);
            Assert.AreEqual(200f / 86400f, state.RegenRatePerSecond);
        }

        [Test]
        public void AddTradeResource_WithNegative_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => _ledger.AddTradeResource(TradeResourceType.Wood, -10f));
        }

        [Test]
        public void SpendTradeResource_WithNegative_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => _ledger.SpendTradeResource(TradeResourceType.Wood, -10f));
        }

        #endregion

        #region Characteristic Tests

        [Test]
        public void AddCharacteristicPoints_IncreasesPointsIntoCurrentLevel()
        {
            _ledger.AddCharacteristicPoints(CharacteristicType.Medicine, 40f, PointsRequired);

            var state = _ledger.GetCharacteristic(CharacteristicType.Medicine);
            Assert.AreEqual(1, state.Level);
            Assert.AreEqual(40f, state.PointsIntoCurrentLevel);
        }

        [Test]
        public void AddCharacteristicPoints_TriggersLevelUp_WhenThresholdReached()
        {
            _ledger.AddCharacteristicPoints(CharacteristicType.Medicine, 100f, PointsRequired);

            var state = _ledger.GetCharacteristic(CharacteristicType.Medicine);
            Assert.AreEqual(2, state.Level);
            Assert.AreEqual(0f, state.PointsIntoCurrentLevel);
        }

        [Test]
        public void AddCharacteristicPoints_CarriesOverExcessPoints_AfterLevelUp()
        {
            _ledger.AddCharacteristicPoints(CharacteristicType.Medicine, 130f, PointsRequired);

            var state = _ledger.GetCharacteristic(CharacteristicType.Medicine);
            Assert.AreEqual(2, state.Level);
            Assert.AreEqual(30f, state.PointsIntoCurrentLevel);
        }

        [Test]
        public void AddCharacteristicPoints_PublishesCharacteristicLeveledUp_OnLevelUp()
        {
            CharacteristicLeveledUp? receivedEvent = null;
            _eventBus.Subscribe<CharacteristicLeveledUp>(e => receivedEvent = e);

            _ledger.AddCharacteristicPoints(CharacteristicType.Medicine, 110f, PointsRequired);

            Assert.IsNotNull(receivedEvent);
            Assert.AreEqual(CharacteristicType.Medicine, receivedEvent.Value.CharacteristicType);
            Assert.AreEqual(2, receivedEvent.Value.NewLevel);
        }

        [Test]
        public void AddCharacteristicPoints_MultipleLevelUpsInOneCall()
        {
            _ledger.AddCharacteristicPoints(CharacteristicType.Medicine, 250f, PointsRequired);

            var state = _ledger.GetCharacteristic(CharacteristicType.Medicine);
            Assert.AreEqual(3, state.Level);
            Assert.AreEqual(50f, state.PointsIntoCurrentLevel);
        }

        [Test]
        public void ReduceCharacteristicPoints_DecreasesPoints()
        {
            _ledger.AddCharacteristicPoints(CharacteristicType.Medicine, 60f, PointsRequired);
            _ledger.ReduceCharacteristicPoints(CharacteristicType.Medicine, 20f, PointsRequired);

            var state = _ledger.GetCharacteristic(CharacteristicType.Medicine);
            Assert.AreEqual(1, state.Level);
            Assert.AreEqual(40f, state.PointsIntoCurrentLevel);
        }

        [Test]
        public void ReduceCharacteristicPoints_CanDropLevels_ButNotBelowPermanentFloor()
        {
            // Adding 150 points: level 1 needs 100 → level up to 2, 50 pts left over.
            // PermanentFloor is now 2 (set on level-up).
            _ledger.AddCharacteristicPoints(CharacteristicType.Medicine, 150f, PointsRequired);

            // Try to reduce by 200 — more than enough to drop to level 1, but floor=2 prevents it.
            _ledger.ReduceCharacteristicPoints(CharacteristicType.Medicine, 200f, PointsRequired);

            var state = _ledger.GetCharacteristic(CharacteristicType.Medicine);
            Assert.AreEqual(2, state.Level); // cannot drop below permanent floor of 2
            Assert.AreEqual(0f, state.PointsIntoCurrentLevel); // clamped to 0 within floor level
        }

        [Test]
        public void ReduceCharacteristicPoints_AtFloor_ClampsToZeroPoints()
        {
            _ledger.ReduceCharacteristicPoints(CharacteristicType.Medicine, 50f, PointsRequired);

            var state = _ledger.GetCharacteristic(CharacteristicType.Medicine);
            Assert.AreEqual(1, state.Level);
            Assert.AreEqual(0f, state.PointsIntoCurrentLevel);
        }

        [Test]
        public void MeetsCharacteristicLevel_ReturnsTrue_WhenAtOrAboveRequired()
        {
            _ledger.AddCharacteristicPoints(CharacteristicType.Medicine, 200f, PointsRequired); // level 3
            
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
        public void PermanentFloor_IsSetOnLevelUp_AndNeverDecreases()
        {
            _ledger.AddCharacteristicPoints(CharacteristicType.Medicine, 100f, PointsRequired); // level 2
            
            var state1 = _ledger.GetCharacteristic(CharacteristicType.Medicine);
            Assert.AreEqual(2, state1.PermanentFloor);

            _ledger.ReduceCharacteristicPoints(CharacteristicType.Medicine, 100f, PointsRequired); // back down
            
            var state2 = _ledger.GetCharacteristic(CharacteristicType.Medicine);
            Assert.AreEqual(2, state2.PermanentFloor); // Floor stays at 2
            Assert.AreEqual(2, state2.Level);
            Assert.AreEqual(0f, state2.PointsIntoCurrentLevel);
        }

        #endregion

        #region Offline Accrual Tests

        [Test]
        public void AccruePassiveResourceRegen_AddsResourcesBasedOnElapsedTime()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 86400f);
            
            var now = DateTime.UtcNow;
            _ledger.GetTradeResource(TradeResourceType.Wood).LastUpdatedUtc = now.AddSeconds(-10);

            _ledger.AccruePassiveResourceRegen(now);

            // Regen rate is Capacity / 86400 = 1/sec
            // 10 seconds elapsed = 10 resources
            Assert.AreEqual(10f, _ledger.GetTradeResource(TradeResourceType.Wood).Amount);
        }

        [Test]
        public void AccruePassiveResourceRegen_CapsAtCapacity()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 100f);
            
            var now = DateTime.UtcNow;
            _ledger.GetTradeResource(TradeResourceType.Wood).LastUpdatedUtc = now.AddDays(-2);

            _ledger.AccruePassiveResourceRegen(now);

            Assert.AreEqual(100f, _ledger.GetTradeResource(TradeResourceType.Wood).Amount);
        }

        [Test]
        public void AccruePassiveResourceRegen_WithNoElapsedTime_AddsNothing()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 100f);
            
            var now = DateTime.UtcNow;
            _ledger.GetTradeResource(TradeResourceType.Wood).LastUpdatedUtc = now;

            _ledger.AccruePassiveResourceRegen(now);

            Assert.AreEqual(0f, _ledger.GetTradeResource(TradeResourceType.Wood).Amount);
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
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 100f);
            _ledger.AddTradeResource(TradeResourceType.Wood, 45f);

            var snapshot = _ledger.TakeSnapshot();

            Assert.IsTrue(snapshot.TradeResourceAmounts.ContainsKey(TradeResourceType.Wood));
            Assert.AreEqual(45f, snapshot.TradeResourceAmounts[TradeResourceType.Wood]);
        }

        [Test]
        public void TakeSnapshot_ReturnsCorrectCharacteristicLevels()
        {
            _ledger.AddCharacteristicPoints(CharacteristicType.Science, 100f, PointsRequired); // level 2

            var snapshot = _ledger.TakeSnapshot();

            Assert.IsTrue(snapshot.CharacteristicLevels.ContainsKey(CharacteristicType.Science));
            Assert.AreEqual(2, snapshot.CharacteristicLevels[CharacteristicType.Science]);
        }

        [Test]
        public void Snapshot_IsImmutable_MutatingLedgerAfterSnapshotDoesNotChangeSnapshot()
        {
            _ledger.SetWarehouseCapacity(TradeResourceType.Wood, 100f);
            _ledger.AddTradeResource(TradeResourceType.Wood, 40f);
            _ledger.AddGold(100);

            var snapshot = _ledger.TakeSnapshot();

            _ledger.AddTradeResource(TradeResourceType.Wood, 10f);
            _ledger.AddGold(50);

            Assert.AreEqual(40f, snapshot.TradeResourceAmounts[TradeResourceType.Wood]);
            Assert.AreEqual(100, snapshot.Gold);
        }

        #endregion
    }
}
