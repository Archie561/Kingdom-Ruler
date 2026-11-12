using System;
using NUnit.Framework;
using KingdomRuler.Systems.Events;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Systems.Save;

namespace KingdomRuler.Tests.EditMode.Systems.Ledger
{
    /// <summary>
    /// The ledger is the one place a bad save turns into a broken economy, so these cover
    /// the round trip and — more importantly — what happens when the file on disk is wrong.
    /// </summary>
    [TestFixture]
    public sealed class KingdomLedgerSaveTests
    {
        private KingdomLedger NewLedger() => new KingdomLedger(new EventBus());

        // ── What the file on disk may hold ────────────────────────────────────────

        private static LedgerStateDto FromJson(string json) =>
            Newtonsoft.Json.JsonConvert.DeserializeObject<LedgerStateDto>(json);

        /// <summary>A NaN or ±Infinity reaches the DTO as 0 through WholeNumberJsonConverter, and 0 is not a capacity.</summary>
        [Test]
        public void Load_NonFiniteValues_FallBackToDefaults()
        {
            var ledger = NewLedger();
            ledger.LoadFromDto(FromJson(@"{ ""tradeResources"": { ""Wood"": { ""amount"": NaN, ""capacity"": Infinity } } }"));

            var wood = ledger.GetTradeResource(TradeResourceType.Wood);
            Assert.AreEqual(0, wood.Amount);
            Assert.AreEqual(100, wood.Capacity);
        }

        /// <summary>Schema 7 stored trade amounts as decimals; such a save must load, not be set aside as corrupt.</summary>
        [Test]
        public void Load_ASchema7FileWithDecimalAmounts_RoundsToWholeNumbers()
        {
            var ledger = NewLedger();
            ledger.LoadFromDto(FromJson(@"{
                ""gold"": 0, ""crystals"": 3,
                ""tradeResources"": {
                    ""Stone"": { ""amount"": 87.34, ""capacity"": 220.0, ""lastUpdatedUtc"": ""2026-11-05T15:42:00.0000000Z"" },
                    ""Wood"":  { ""amount"": 12.5,  ""capacity"": 150.0, ""lastUpdatedUtc"": ""2026-11-05T15:42:00.0000000Z"" }
                } }"));

            Assert.AreEqual(87,  ledger.GetTradeResource(TradeResourceType.Stone).Amount);
            Assert.AreEqual(220, ledger.GetTradeResource(TradeResourceType.Stone).Capacity);
            Assert.AreEqual(13,  ledger.GetTradeResource(TradeResourceType.Wood).Amount, "Halves round away from zero.");
        }

        [Test]
        public void RoundTrip_WritesIntegers()
        {
            var source = NewLedger();
            source.SetWarehouseCapacity(TradeResourceType.Wood, 500);
            source.AddTradeResource(TradeResourceType.Wood, 321);

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(source.ToDto());

            StringAssert.Contains(@"""amount"":321,", json);
            StringAssert.Contains(@"""capacity"":500,", json);
        }

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
            source.AddCharacteristicPoints(CharacteristicType.Army, 250);      // → level 3, 10 in
            source.AddCharacteristicPoints(CharacteristicType.Medicine, 40);   // → level 1, 40 in

            var restored = NewLedger();
            restored.LoadFromDto(source.ToDto());

            foreach (CharacteristicType type in Enum.GetValues(typeof(CharacteristicType)))
            {
                var a = source.GetCharacteristic(type);
                var b = restored.GetCharacteristic(type);
                Assert.AreEqual(a.Level, b.Level, $"{type} level");
                Assert.AreEqual(a.PointsIntoCurrentLevel, b.PointsIntoCurrentLevel, $"{type} points");
            }
        }

        [Test]
        public void RoundTrip_PreservesCapacityAmountAndRegenProgress()
        {
            var start  = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var source = NewLedger();
            source.SetWarehouseCapacity(TradeResourceType.Wood, 500);
            source.ResetRegenBaseline(start);
            source.AddTradeResource(TradeResourceType.Wood, 321);
            source.AccruePassiveResourceRegen(start.AddMinutes(7));   // 2 units, and part of a third

            var restored = NewLedger();
            restored.LoadFromDto(source.ToDto());

            var a = source.GetTradeResource(TradeResourceType.Wood);
            var b = restored.GetTradeResource(TradeResourceType.Wood);
            Assert.AreEqual(a.Capacity, b.Capacity);
            Assert.AreEqual(a.Amount, b.Amount);
            Assert.AreEqual(a.LastUpdatedUtc, b.LastUpdatedUtc, "The partial unit lives in the timestamp.");

            var later = start.AddMinutes(30);
            source.AccruePassiveResourceRegen(later);
            restored.AccruePassiveResourceRegen(later);
            Assert.AreEqual(source.GetTradeResource(TradeResourceType.Wood).Amount,
                            restored.GetTradeResource(TradeResourceType.Wood).Amount);
        }

        [Test]
        public void RoundTrip_ThroughRealJson_Survives()
        {
            var source = NewLedger();
            source.AddGold(999);
            source.AddCharacteristicPoints(CharacteristicType.Science, 175);
            source.SetWarehouseCapacity(TradeResourceType.Clay, 250);

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(source.ToDto());
            var dto  = Newtonsoft.Json.JsonConvert.DeserializeObject<LedgerStateDto>(json);

            var restored = NewLedger();
            restored.LoadFromDto(dto);

            Assert.AreEqual(999, restored.Gold);
            Assert.AreEqual(source.GetCharacteristic(CharacteristicType.Science).Level,
                            restored.GetCharacteristic(CharacteristicType.Science).Level);
            Assert.AreEqual(250, restored.GetTradeResource(TradeResourceType.Clay).Capacity);
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
        public void Load_GoldWrittenAsADecimal_StillLoads()
        {
            var ledger = NewLedger();
            ledger.LoadFromDto(FromJson(@"{ ""gold"": 1000.0, ""crystals"": 7 }"));

            Assert.AreEqual(1000, ledger.Gold);
            Assert.AreEqual(7, ledger.Crystals, "The rest of the section still loads.");
        }

        [Test]
        public void LoadFromDto_GoldAboveTheCeiling_ClampsToIt()
        {
            var ledger = NewLedger();
            ledger.LoadFromDto(new LedgerStateDto { Gold = long.MaxValue });

            Assert.AreEqual(KingdomLedger.MaxGold, ledger.Gold);
        }

        [Test]
        public void Load_AHandEditedHugeGold_FromJson_ClampsToTheCeiling()
        {
            var ledger = NewLedger();
            ledger.LoadFromDto(FromJson(@"{ ""gold"": 9000000000000000000 }"));

            Assert.AreEqual(KingdomLedger.MaxGold, ledger.Gold);
        }

        [Test]
        public void RoundTrip_AtTheCeiling_Survives()
        {
            var source = NewLedger();
            source.SetGold(KingdomLedger.MaxGold);

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(source.ToDto());
            var restored = NewLedger();
            restored.LoadFromDto(Newtonsoft.Json.JsonConvert.DeserializeObject<LedgerStateDto>(json));

            Assert.AreEqual(KingdomLedger.MaxGold, restored.Gold);
        }

        [Test]
        public void LoadFromDto_UnknownEnumNames_AreSkippedNotThrown()
        {
            var dto = new LedgerStateDto();
            dto.TradeResources["Unobtainium"]  = new TradeResourceStateDto { Amount = 5, Capacity = 10 };
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
                new CharacteristicStateDto { Level = 0, PointsIntoCurrentLevel = 0 };

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
                new CharacteristicStateDto { Level = 1, PointsIntoCurrentLevel = 999_999 };

            var ledger = NewLedger();
            ledger.LoadFromDto(dto);

            var state = ledger.GetCharacteristic(CharacteristicType.Army);
            Assert.AreEqual(1, state.Level);
            Assert.Less(state.PointsIntoCurrentLevel, ledger.PointsRequiredForLevel(1));

            // And the clamp must not have created a pending phantom level-up.
            int levelUps = 0;
            var bus = new EventBus();
            var check = new KingdomLedger(bus);
            bus.Subscribe<CharacteristicLeveledUpEvent>(_ => levelUps++);
            check.LoadFromDto(dto);
            Assert.AreEqual(0, levelUps, "Hydration must not publish level-up events.");
        }

        [Test]
        public void LoadFromDto_NegativeProgress_ClampsToZero()
        {
            var dto = new LedgerStateDto();
            dto.Characteristics[CharacteristicType.Army.ToString()] =
                new CharacteristicStateDto { Level = 2, PointsIntoCurrentLevel = -50 };

            var ledger = NewLedger();
            ledger.LoadFromDto(dto);

            Assert.AreEqual(0, ledger.GetCharacteristic(CharacteristicType.Army).PointsIntoCurrentLevel);
        }

        [Test]
        public void LoadFromDto_NonPositiveCapacity_KeepsTheDefaultRatherThanBrickingRegen()
        {
            var dto = new LedgerStateDto();
            dto.TradeResources[TradeResourceType.Stone.ToString()] =
                new TradeResourceStateDto { Capacity = 0, Amount = 0 };

            var ledger = NewLedger();
            ledger.LoadFromDto(dto);

            var state = ledger.GetTradeResource(TradeResourceType.Stone);
            Assert.Greater(state.Capacity, 0, "A zero capacity would make the warehouse unusable.");

            ledger.AccruePassiveResourceRegen(state.LastUpdatedUtc.AddDays(1));
            Assert.AreEqual(state.Capacity, state.Amount, "It must still refill in a day.");
        }

        [Test]
        public void LoadFromDto_AmountAboveCapacity_ClampsToCapacity()
        {
            var dto = new LedgerStateDto();
            dto.TradeResources[TradeResourceType.Metal.ToString()] =
                new TradeResourceStateDto { Capacity = 100, Amount = 10_000 };

            var ledger = NewLedger();
            ledger.LoadFromDto(dto);

            Assert.AreEqual(100, ledger.GetTradeResource(TradeResourceType.Metal).Amount);
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
            Assert.Greater(ledger.GetTradeResource(TradeResourceType.Leather).Capacity, 0);
        }

        [Test]
        public void LoadFromDto_UnparsableTimestamp_FallsBackWithoutThrowing()
        {
            var dto = new LedgerStateDto();
            dto.TradeResources[TradeResourceType.Wood.ToString()] = new TradeResourceStateDto
            {
                Capacity = 100, Amount = 10, LastUpdatedUtc = "not-a-date"
            };

            var ledger = NewLedger();
            Assert.DoesNotThrow(() => ledger.LoadFromDto(dto));
            Assert.AreEqual(10, ledger.GetTradeResource(TradeResourceType.Wood).Amount);
        }

        [Test]
        public void Hydration_DoesNotPublishResourceOrCurrencyEvents()
        {
            var source = NewLedger();
            source.AddGold(500);
            source.AddCrystals(5);
            source.AddTradeResource(TradeResourceType.Wood, 20);

            var bus = new EventBus();
            int events = 0;
            bus.Subscribe<GoldChangedEvent>(_ => events++);
            bus.Subscribe<CrystalsChangedEvent>(_ => events++);
            bus.Subscribe<TradeResourceChangedEvent>(_ => events++);

            new KingdomLedger(bus).LoadFromDto(source.ToDto());

            Assert.AreEqual(0, events, "Loading a save is not a gameplay change; nothing should be announced.");
        }
    }
}
