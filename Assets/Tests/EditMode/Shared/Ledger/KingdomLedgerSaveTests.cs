using System;
using NUnit.Framework;
using KingdomRuler.Core;
using KingdomRuler.Shared.Ledger;
using KingdomRuler.Shared.Services;

namespace KingdomRuler.Tests.EditMode.Shared.Ledger
{
    /// <summary>
    /// The ledger is the one place a bad save turns into a broken economy, so these cover
    /// the round trip and — more importantly — what happens when the file on disk is wrong.
    /// </summary>
    [TestFixture]
    public sealed class KingdomLedgerSaveTests
    {
        private KingdomLedger NewLedger() => new KingdomLedger(new EventBus());

        // ── Round trip ────────────────────────────────────────────────────────────

        [Test]
        public void RoundTrip_PreservesGoldAndCrystals()
        {
            var source = NewLedger();
            source.AddGold(12_345);
            source.AddCrystals(67);

            var restored = NewLedger();
            restored.LoadFromDto(source.ToDto());

            Assert.AreEqual(12_345, restored.Gold);
            Assert.AreEqual(67, restored.Crystals);
        }

        [Test]
        public void RoundTrip_PreservesEveryCharacteristic()
        {
            var source = NewLedger();
            source.AddCharacteristicPoints(CharacteristicType.Army, 250f);      // → level 3, 10 in
            source.AddCharacteristicPoints(CharacteristicType.Medicine, 40f);   // → level 1, 40 in

            var restored = NewLedger();
            restored.LoadFromDto(source.ToDto());

            foreach (CharacteristicType type in Enum.GetValues(typeof(CharacteristicType)))
            {
                var a = source.GetCharacteristic(type);
                var b = restored.GetCharacteristic(type);
                Assert.AreEqual(a.Level, b.Level, $"{type} level");
                Assert.AreEqual(a.PointsIntoCurrentLevel, b.PointsIntoCurrentLevel, 0.001f, $"{type} points");
            }
        }

        [Test]
        public void RoundTrip_PreservesWarehouseCapacityAmountAndRegenRate()
        {
            var source = NewLedger();
            source.SetWarehouseCapacity(TradeResourceType.Wood, 500f);
            source.AddTradeResource(TradeResourceType.Wood, 321f);

            var restored = NewLedger();
            restored.LoadFromDto(source.ToDto());

            var a = source.GetTradeResource(TradeResourceType.Wood);
            var b = restored.GetTradeResource(TradeResourceType.Wood);
            Assert.AreEqual(a.Capacity, b.Capacity, 0.001f);
            Assert.AreEqual(a.Amount, b.Amount, 0.001f);
            Assert.AreEqual(a.RegenRatePerSecond, b.RegenRatePerSecond, 0.000001f);
            Assert.AreEqual(a.LastUpdatedUtc, b.LastUpdatedUtc);
        }

        [Test]
        public void RoundTrip_ThroughRealJson_Survives()
        {
            var source = NewLedger();
            source.AddGold(999);
            source.AddCharacteristicPoints(CharacteristicType.Science, 175f);
            source.SetWarehouseCapacity(TradeResourceType.Clay, 250f);

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(source.ToDto());
            var dto  = Newtonsoft.Json.JsonConvert.DeserializeObject<LedgerStateDto>(json);

            var restored = NewLedger();
            restored.LoadFromDto(dto);

            Assert.AreEqual(999, restored.Gold);
            Assert.AreEqual(source.GetCharacteristic(CharacteristicType.Science).Level,
                            restored.GetCharacteristic(CharacteristicType.Science).Level);
            Assert.AreEqual(250f, restored.GetTradeResource(TradeResourceType.Clay).Capacity, 0.001f);
        }

        // ── Hostile / damaged input ───────────────────────────────────────────────

        [Test]
        public void LoadFromDto_Null_LeavesLedgerAtDefaults()
        {
            var ledger = NewLedger();
            Assert.DoesNotThrow(() => ledger.LoadFromDto(null));
            Assert.AreEqual(0, ledger.Gold);
            Assert.AreEqual(1, ledger.GetCharacteristic(CharacteristicType.Army).Level);
        }

        [Test]
        public void LoadFromDto_NegativeCurrency_ClampsToZero()
        {
            var ledger = NewLedger();
            ledger.LoadFromDto(new LedgerStateDto { Gold = -5000, Crystals = -10 });

            Assert.AreEqual(0, ledger.Gold);
            Assert.AreEqual(0, ledger.Crystals);
        }

        [Test]
        public void LoadFromDto_UnknownEnumNames_AreSkippedNotThrown()
        {
            var dto = new LedgerStateDto();
            dto.TradeResources["Unobtainium"]  = new TradeResourceStateDto { Amount = 5f, Capacity = 10f };
            dto.Characteristics["Necromancy"]  = new CharacteristicStateDto { Level = 9 };

            var ledger = NewLedger();
            Assert.DoesNotThrow(() => ledger.LoadFromDto(dto));
            Assert.AreEqual(1, ledger.GetCharacteristic(CharacteristicType.Army).Level);
        }

        [Test]
        public void LoadFromDto_LevelBelowOne_ClampsToOne()
        {
            var dto = new LedgerStateDto();
            dto.Characteristics[CharacteristicType.Army.ToString()] =
                new CharacteristicStateDto { Level = 0, PointsIntoCurrentLevel = 0f };

            var ledger = NewLedger();
            ledger.LoadFromDto(dto);

            Assert.AreEqual(1, ledger.GetCharacteristic(CharacteristicType.Army).Level);
        }

        /// <summary>
        /// Progress at or beyond the level threshold would mean the very next point awarded
        /// triggers a level-up the player never earned. Clamp it inside the level.
        /// </summary>
        [Test]
        public void LoadFromDto_ProgressBeyondThreshold_ClampsInsideTheLevel()
        {
            var dto = new LedgerStateDto();
            dto.Characteristics[CharacteristicType.Army.ToString()] =
                new CharacteristicStateDto { Level = 1, PointsIntoCurrentLevel = 999_999f };

            var ledger = NewLedger();
            ledger.LoadFromDto(dto);

            var state = ledger.GetCharacteristic(CharacteristicType.Army);
            Assert.AreEqual(1, state.Level);
            Assert.Less(state.PointsIntoCurrentLevel, ledger.PointsRequiredForLevel(1));

            // And the clamp must not have created a pending phantom level-up.
            int levelUps = 0;
            var bus = new EventBus();
            var check = new KingdomLedger(bus);
            bus.Subscribe<CharacteristicLeveledUp>(_ => levelUps++);
            check.LoadFromDto(dto);
            Assert.AreEqual(0, levelUps, "Hydration must not publish level-up events.");
        }

        [Test]
        public void LoadFromDto_NegativeProgress_ClampsToZero()
        {
            var dto = new LedgerStateDto();
            dto.Characteristics[CharacteristicType.Army.ToString()] =
                new CharacteristicStateDto { Level = 2, PointsIntoCurrentLevel = -50f };

            var ledger = NewLedger();
            ledger.LoadFromDto(dto);

            Assert.AreEqual(0f, ledger.GetCharacteristic(CharacteristicType.Army).PointsIntoCurrentLevel);
        }

        [Test]
        public void LoadFromDto_NonPositiveCapacity_KeepsTheDefaultRatherThanBrickingRegen()
        {
            var dto = new LedgerStateDto();
            dto.TradeResources[TradeResourceType.Stone.ToString()] =
                new TradeResourceStateDto { Capacity = 0f, Amount = 0f, RegenRatePerSecond = 0f };

            var ledger = NewLedger();
            ledger.LoadFromDto(dto);

            var state = ledger.GetTradeResource(TradeResourceType.Stone);
            Assert.Greater(state.Capacity, 0f, "A zero capacity would make the warehouse unusable.");
            Assert.Greater(state.RegenRatePerSecond, 0f, "A zero regen rate would stall the resource forever.");
        }

        [Test]
        public void LoadFromDto_AmountAboveCapacity_ClampsToCapacity()
        {
            var dto = new LedgerStateDto();
            dto.TradeResources[TradeResourceType.Metal.ToString()] =
                new TradeResourceStateDto { Capacity = 100f, Amount = 10_000f, RegenRatePerSecond = 1f };

            var ledger = NewLedger();
            ledger.LoadFromDto(dto);

            Assert.AreEqual(100f, ledger.GetTradeResource(TradeResourceType.Metal).Amount, 0.001f);
        }

        [Test]
        public void LoadFromDto_MissingSections_LeaveDefaultsIntact()
        {
            var ledger = NewLedger();
            ledger.LoadFromDto(new LedgerStateDto
            {
                Gold = 10, Crystals = 2, TradeResources = null, Characteristics = null
            });

            Assert.AreEqual(10, ledger.Gold);
            Assert.AreEqual(1, ledger.GetCharacteristic(CharacteristicType.Welfare).Level);
            Assert.Greater(ledger.GetTradeResource(TradeResourceType.Leather).Capacity, 0f);
        }

        [Test]
        public void LoadFromDto_UnparsableTimestamp_FallsBackWithoutThrowing()
        {
            var dto = new LedgerStateDto();
            dto.TradeResources[TradeResourceType.Wood.ToString()] = new TradeResourceStateDto
            {
                Capacity = 100f, Amount = 10f, RegenRatePerSecond = 1f, LastUpdatedUtc = "not-a-date"
            };

            var ledger = NewLedger();
            Assert.DoesNotThrow(() => ledger.LoadFromDto(dto));
            Assert.AreEqual(10f, ledger.GetTradeResource(TradeResourceType.Wood).Amount, 0.001f);
        }

        [Test]
        public void Hydration_DoesNotPublishResourceOrCurrencyEvents()
        {
            var source = NewLedger();
            source.AddGold(500);
            source.AddCrystals(5);
            source.AddTradeResource(TradeResourceType.Wood, 20f);

            var bus = new EventBus();
            int events = 0;
            bus.Subscribe<GoldChanged>(_ => events++);
            bus.Subscribe<CrystalsChanged>(_ => events++);
            bus.Subscribe<ResourceChanged>(_ => events++);

            new KingdomLedger(bus).LoadFromDto(source.ToDto());

            Assert.AreEqual(0, events, "Loading a save is not a gameplay change; nothing should be announced.");
        }
    }
}
