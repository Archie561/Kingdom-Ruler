using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using KingdomRuler.Modules.Economy;
using KingdomRuler.Systems.Events;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Systems.Save;
using KingdomRuler.Tests.EditMode.Systems;

namespace KingdomRuler.Tests.EditMode.Modules.Economy
{
    /// <summary>
    /// The mechanic: the starting gold, buying (quoted == charged), "Max", the crystal top-up as one
    /// transaction, storages filling and collecting, the clock, and a defensive save.
    /// </summary>
    /// <remarks>
    /// The bakery used throughout makes 60 gold an hour per unit: a 1,440-coin storage, one coin a
    /// minute — or every 30 s with two units.
    /// </remarks>
    [TestFixture]
    public sealed class EconomyManagerTests
    {
        private const string Bakery = "business.bakery";
        private const string Tavern = "business.tavern";

        private readonly List<UnityEngine.Object> _assets = new();

        private FakeClock      _clock;
        private EventBus       _bus;
        private KingdomLedger  _ledger;
        private EconomyConfig  _config;
        private EconomyManager _manager;

        [SetUp]
        public void SetUp()
        {
            _clock  = new FakeClock();
            _bus    = new EventBus();
            _ledger = new KingdomLedger(_bus);

            // The tavern is listed first on purpose: the starting gold is the cheapest, not the first.
            _config  = Config(Business(Tavern, price: 1_500, perHour: 1_000), Business(Bakery, price: 100, perHour: 60));
            _manager = new EconomyManager(_ledger, _clock, _config);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in _assets) UnityEngine.Object.DestroyImmediate(asset);
            _assets.Clear();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        [Test]
        public void NewGame_StartsWithExactlyTheCheapestBusinessPrice()
        {
            _manager.InitializeNewGame();

            Assert.AreEqual(100, _manager.StartingGold);
            Assert.AreEqual(100, _ledger.Gold);
        }

        [Test]
        public void NewGame_OnAnExistingBalance_AddsTheStartingGold()
        {
            // A save from before Economy existed: its gold is kept and the start is added.
            _ledger.AddGold(500);

            _manager.InitializeNewGame();

            Assert.AreEqual(600, _ledger.Gold);
        }

        [Test]
        public void NewGame_OwnsNothing_AndProducesNothing()
        {
            _manager.InitializeNewGame();
            Elapse(TimeSpan.FromHours(10));

            Assert.AreEqual(0, _manager.UnitsOwned(Bakery));
            Assert.AreEqual(0, _manager.GoldPerHour(Bakery));
            Assert.AreEqual(0, _manager.StorageCapacity(Bakery));
            Assert.AreEqual(0, _manager.Stored(Bakery));
            Assert.IsFalse(_manager.IsStorageFull(Bakery), "A business with no units has no storage to be full.");
        }

        [Test]
        public void NewGame_WithNoBusinesses_GrantsNothing()
        {
            var manager = new EconomyManager(_ledger, _clock, Config());

            manager.InitializeNewGame();

            Assert.AreEqual(0, manager.StartingGold);
            Assert.AreEqual(0, _ledger.Gold);
        }

        [Test]
        public void EmptySlotsAndRepeatedIds_AreSkippedWithAnError()
        {
            LogAssert.Expect(LogType.Error, new Regex("empty business"));
            LogAssert.Expect(LogType.Error, new Regex("listed twice"));

            var manager = new EconomyManager(_ledger, _clock,
                Config(Business(Bakery, 100, 60), null, Business(Bakery, 5, 5), Business(Tavern, 1_500, 1_000)));

            CollectionAssert.AreEqual(new[] { Bakery, Tavern }, IdsOf(manager.Businesses));
            Assert.AreEqual(100, manager.PriceOf(Bakery, 1), "The first of a repeated id is kept.");
        }

        // ── Buying for gold ───────────────────────────────────────────────────────

        [TestCase(1)]
        [TestCase(10)]
        public void TryBuy_ChargesExactlyThePriceQuoted(int count)
        {
            _ledger.AddGold(5_000);
            long quoted = _manager.PriceOf(Bakery, count);

            Assert.IsTrue(_manager.CanAfford(Bakery, count));
            Assert.IsTrue(_manager.TryBuy(Bakery, count));

            Assert.AreEqual(100L * count, quoted, "Flat: every unit costs the same.");
            Assert.AreEqual(5_000 - quoted, _ledger.Gold);
            Assert.AreEqual(count, _manager.UnitsOwned(Bakery));
        }

        [Test]
        public void TryBuy_ThePriceNeverGrowsWithUnitsOwned()
        {
            _ledger.AddGold(100_000);
            _manager.TryBuy(Bakery, 50);

            Assert.AreEqual(100, _manager.PriceOf(Bakery, 1), "The 51st bakery costs what the first did.");
        }

        [Test]
        public void TryBuy_WhenShort_IsRefusedAndChangesNothing()
        {
            _ledger.AddGold(99);

            Assert.IsFalse(_manager.CanAfford(Bakery, 1));
            Assert.IsFalse(_manager.TryBuy(Bakery, 1));
            Assert.AreEqual(99, _ledger.Gold);
            Assert.AreEqual(0, _manager.UnitsOwned(Bakery));
        }

        [Test]
        public void TryBuy_ANonPositiveCountOrAnUnknownId_IsRefused()
        {
            _ledger.AddGold(5_000);

            Assert.IsFalse(_manager.TryBuy(Bakery, 0));
            Assert.IsFalse(_manager.TryBuy(Bakery, -1));
            Assert.IsFalse(_manager.TryBuy("business.unknown", 1));
            Assert.IsFalse(_manager.TryBuy(null, 1));
            Assert.AreEqual(5_000, _ledger.Gold);
        }

        [Test]
        public void TryBuy_RaisesTheRateAndTheCapacityTogether()
        {
            _ledger.AddGold(300);
            _manager.TryBuy(Bakery, 3);

            Assert.AreEqual(180, _manager.GoldPerHour(Bakery));
            Assert.AreEqual(4_320, _manager.StorageCapacity(Bakery), "24 hours of the type's total production.");
        }

        [Test]
        public void MaxAffordable_IsWhatTheGoldPaysFor_AndZeroWhenNotOneIs()
        {
            _ledger.AddGold(1_050);
            Assert.AreEqual(10, _manager.MaxAffordable(Bakery));

            _manager.TryBuy(Bakery, _manager.MaxAffordable(Bakery));
            Assert.AreEqual(50, _ledger.Gold);
            Assert.AreEqual(0, _manager.MaxAffordable(Bakery), "Nothing affordable: the crystal offer is for one unit (GDD §8).");
        }

        [Test]
        public void TheUnitsCeiling_ClampsMax_AndRefusesAnyMore()
        {
            _ledger.SetGold(KingdomLedger.MaxGold);

            Assert.AreEqual(EconomyConfig.MaxUnitsPerType, _manager.MaxAffordable(Bakery));
            Assert.IsTrue(_manager.TryBuy(Bakery, _manager.MaxAffordable(Bakery)));
            Assert.AreEqual(0, _manager.RoomFor(Bakery));

            long goldBefore = _ledger.Gold;
            Assert.IsFalse(_manager.CanAfford(Bakery, 1));
            Assert.IsFalse(_manager.TryBuy(Bakery, 1));
            Assert.AreEqual(goldBefore, _ledger.Gold);
        }

        [Test]
        public void TryBuy_SettlesFirst_SoTimeAlreadyPassedCountsAtTheOldRate()
        {
            _ledger.AddGold(200);
            _manager.TryBuy(Bakery, 1);

            _clock.Advance(TimeSpan.FromMinutes(30));   // no tick: only the purchase settles
            _manager.TryBuy(Bakery, 1);
            Assert.AreEqual(30, _manager.Stored(Bakery), "Half an hour at 60 an hour, before the second unit.");

            Elapse(TimeSpan.FromMinutes(30));
            Assert.AreEqual(90, _manager.Stored(Bakery), "Then half an hour at 120 an hour.");
        }

        [Test]
        public void TryBuy_KeepsTheCoinInProgress()
        {
            // Half a coin at one unit (30 s of 60), then a second unit: coins now take 30 s, so the
            // time already spent completes one. Restarting the coin would give none.
            _ledger.AddGold(200);
            _manager.TryBuy(Bakery, 1);

            _clock.Advance(TimeSpan.FromSeconds(30));
            _manager.TryBuy(Bakery, 1);
            _clock.Tick();

            Assert.AreEqual(1, _manager.Stored(Bakery));
        }

        // ── Storages ──────────────────────────────────────────────────────────────

        [Test]
        public void AStorage_FillsInExactlyStorageHours_ThenStops()
        {
            BuyOneBakery();

            Elapse(TimeSpan.FromHours(24) - TimeSpan.FromMinutes(1));
            Assert.AreEqual(1_439, _manager.Stored(Bakery));
            Assert.IsFalse(_manager.IsStorageFull(Bakery));

            Elapse(TimeSpan.FromMinutes(1));
            Assert.AreEqual(1_440, _manager.Stored(Bakery));
            Assert.IsTrue(_manager.IsStorageFull(Bakery));

            Elapse(TimeSpan.FromDays(2));
            Assert.AreEqual(1_440, _manager.Stored(Bakery), "A full storage stops filling.");
        }

        [Test]
        public void TheClockAlone_SettlesTheStorages()
        {
            BuyOneBakery();

            _clock.Advance(TimeSpan.FromHours(1));
            Assert.AreEqual(0, _manager.Stored(Bakery), "Nothing settles before a tick.");

            _clock.Tick();
            Assert.AreEqual(60, _manager.Stored(Bakery));
        }

        [Test]
        public void StoragesFilled_IsRaisedOnce_WhenAStorageBecomesFull()
        {
            BuyOneBakery();
            int filled = 0;
            _manager.StoragesFilled += () => filled++;

            Elapse(TimeSpan.FromHours(23));
            Assert.AreEqual(0, filled);

            Elapse(TimeSpan.FromHours(1));
            Assert.AreEqual(1, filled);

            Elapse(TimeSpan.FromHours(5));
            Assert.AreEqual(1, filled, "Not again while it stays full.");
        }

        [Test]
        public void StoragesFilled_AfterACollect_IsRaisedAgainWhenItRefills()
        {
            BuyOneBakery();
            int filled = 0;
            _manager.StoragesFilled += () => filled++;

            Elapse(TimeSpan.FromHours(24));
            _manager.Collect(Bakery);
            Elapse(TimeSpan.FromHours(24));

            Assert.AreEqual(2, filled);
        }

        [Test]
        public void StoragesFilled_IsRaisedOncePerTick_HoweverManyFillTogether()
        {
            _ledger.AddGold(1_600);
            _manager.TryBuy(Bakery, 1);
            _manager.TryBuy(Tavern, 1);
            int filled = 0;
            _manager.StoragesFilled += () => filled++;

            Elapse(TimeSpan.FromHours(30));   // both fill while away: one sound on resume

            Assert.AreEqual(1, filled);
        }

        [Test]
        public void StoragesFilled_IsNeverRaised_ForAStorageThatFilledWhileTheAppWasClosed()
        {
            BuyOneBakery();
            Elapse(TimeSpan.FromHours(12));
            var dto = _manager.ToDto();

            _clock.Advance(TimeSpan.FromHours(20));
            var reloaded = new EconomyManager(_ledger, _clock, _config);
            int filled = 0;
            reloaded.StoragesFilled += () => filled++;
            reloaded.LoadFromDto(dto);
            _clock.Tick();

            Assert.IsTrue(reloaded.IsStorageFull(Bakery));
            Assert.AreEqual(0, filled, "Full at load is not 'became full': no sound at startup.");
        }

        // ── Collecting ────────────────────────────────────────────────────────────

        [Test]
        public void Collect_MovesTheStorageIntoTheBalance_AndEmptiesIt()
        {
            BuyOneBakery();   // spends the 100, leaving 0
            Elapse(TimeSpan.FromHours(1));

            Assert.AreEqual(60, _manager.Collect(Bakery));
            Assert.AreEqual(60, _ledger.Gold);
            Assert.AreEqual(0, _manager.Stored(Bakery));
        }

        [Test]
        public void Collect_SettlesFirst()
        {
            BuyOneBakery();
            _clock.Advance(TimeSpan.FromHours(1));   // no tick

            Assert.AreEqual(60, _manager.Collect(Bakery));
        }

        [Test]
        public void Collect_PublishesOneGoldChange()
        {
            BuyOneBakery();
            Elapse(TimeSpan.FromHours(1));
            var changes = new List<GoldChangedEvent>();
            _bus.Subscribe<GoldChangedEvent>(e => changes.Add(e));

            _manager.Collect(Bakery);

            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(60, changes[0].Delta);
        }

        [Test]
        public void Collect_FromAFullStorage_StartsFillingAgainFromNow()
        {
            BuyOneBakery();
            Elapse(TimeSpan.FromHours(34));   // full for ten hours

            Assert.AreEqual(1_440, _manager.Collect(Bakery));

            Elapse(TimeSpan.FromMinutes(1));
            Assert.AreEqual(1, _manager.Stored(Bakery), "A full storage banks nothing: no burst after collecting.");
        }

        [Test]
        public void Collect_AtTheGoldCeiling_LeavesTheRestInTheStorage()
        {
            BuyOneBakery();
            Elapse(TimeSpan.FromHours(1));
            _ledger.SetGold(KingdomLedger.MaxGold - 25);

            Assert.AreEqual(25, _manager.Collect(Bakery));
            Assert.AreEqual(KingdomLedger.MaxGold, _ledger.Gold);
            Assert.AreEqual(35, _manager.Stored(Bakery), "Nothing is silently lost.");
        }

        [Test]
        public void Collect_AnEmptyStorageOrAnUnknownId_MovesNothing()
        {
            Assert.AreEqual(0, _manager.Collect(Bakery));
            Assert.AreEqual(0, _manager.Collect("business.unknown"));
            Assert.AreEqual(0, _ledger.Gold);
        }

        // ── The crystal top-up ────────────────────────────────────────────────────

        [TestCase(640L, 10, 360L, 4L)]   // plan §5: ×10 bakeries holding 640
        [TestCase(40L,  1,  60L,  1L)]   // Max with nothing affordable: one unit
        [TestCase(0L,   1,  100L, 1L)]
        [TestCase(800L, 10, 200L, 2L)]   // an exact multiple
        public void CrystalTopUpCost_CoversExactlyTheShortfall(long gold, int count, long shortfall, long crystals)
        {
            _ledger.AddGold(gold);

            Assert.AreEqual(shortfall, _manager.GoldShortfall(Bakery, count));
            Assert.AreEqual(crystals, _manager.CrystalTopUpCost(Bakery, count));
        }

        [Test]
        public void CrystalTopUpCost_IsZero_WhenTheGoldIsEnough()
        {
            _ledger.AddGold(1_000);

            Assert.AreEqual(0, _manager.GoldShortfall(Bakery, 10));
            Assert.AreEqual(0, _manager.CrystalTopUpCost(Bakery, 10));
            Assert.IsFalse(_manager.CanAffordCrystalTopUp(Bakery, 10), "Nothing to cover: the plain purchase applies.");
            Assert.IsFalse(_manager.TryBuyWithCrystalTopUp(Bakery, 10));
            Assert.AreEqual(1_000, _ledger.Gold);
        }

        [Test]
        public void TryBuyWithCrystalTopUp_ChargesExactlyTheQuotedCrystals_AndAllTheGold()
        {
            _ledger.AddGold(640);
            _ledger.AddCrystals(10);
            long quoted = _manager.CrystalTopUpCost(Bakery, 10);

            Assert.IsTrue(_manager.CanAffordCrystalTopUp(Bakery, 10));
            Assert.IsTrue(_manager.TryBuyWithCrystalTopUp(Bakery, 10));

            Assert.AreEqual(4, quoted);
            Assert.AreEqual(10 - quoted, _ledger.Crystals);
            Assert.AreEqual(0, _ledger.Gold, "The missing gold was added and the price paid: nothing is left.");
            Assert.AreEqual(10, _manager.UnitsOwned(Bakery));
        }

        [Test]
        public void TryBuyWithCrystalTopUp_IsOneChangeToEachCurrency()
        {
            _ledger.AddGold(640);
            _ledger.AddCrystals(10);
            var gold     = new List<GoldChangedEvent>();
            var crystals = new List<CrystalsChangedEvent>();
            _bus.Subscribe<GoldChangedEvent>(e => gold.Add(e));
            _bus.Subscribe<CrystalsChangedEvent>(e => crystals.Add(e));

            _manager.TryBuyWithCrystalTopUp(Bakery, 10);

            Assert.AreEqual(1, gold.Count, "No subscriber sees the topped-up balance in between.");
            Assert.AreEqual(-640, gold[0].Delta);
            Assert.AreEqual(1, crystals.Count);
            Assert.AreEqual(-4, crystals[0].Delta);
        }

        [Test]
        public void TryBuyWithCrystalTopUp_WithNoGoldAtAll_ChangesOnlyTheCrystals()
        {
            _ledger.AddCrystals(5);
            int goldChanges = 0;
            _bus.Subscribe<GoldChangedEvent>(_ => goldChanges++);

            Assert.IsTrue(_manager.TryBuyWithCrystalTopUp(Bakery, 1));
            Assert.AreEqual(4, _ledger.Crystals);
            Assert.AreEqual(0, goldChanges, "No empty gold change.");
        }

        [Test]
        public void TryBuyWithCrystalTopUp_ShortOfCrystals_IsRefusedAndChangesNothing()
        {
            _ledger.AddGold(640);
            _ledger.AddCrystals(3);

            Assert.IsFalse(_manager.CanAffordCrystalTopUp(Bakery, 10));
            Assert.IsFalse(_manager.TryBuyWithCrystalTopUp(Bakery, 10));

            Assert.AreEqual(640, _ledger.Gold);
            Assert.AreEqual(3, _ledger.Crystals);
            Assert.AreEqual(0, _manager.UnitsOwned(Bakery));
        }

        [Test]
        public void TryBuyWithCrystalTopUp_PastTheUnitsCeiling_IsRefused()
        {
            _ledger.SetGold(KingdomLedger.MaxGold);
            _manager.TryBuy(Bakery, EconomyConfig.MaxUnitsPerType);
            _ledger.SetGold(0);
            _ledger.AddCrystals(1_000);

            Assert.IsFalse(_manager.TryBuyWithCrystalTopUp(Bakery, 1));
            Assert.AreEqual(1_000, _ledger.Crystals);
        }

        [Test]
        public void ACrystalPriceBeyondAnyBalance_IsUnaffordable_NotAnError()
        {
            // 1,000 units at 10¹² each, short by 10¹⁵: 10¹³ crystals, far past int.
            var manager = new EconomyManager(_ledger, _clock, Config(Business("business.vault", 1_000_000_000_000, 1)));
            _ledger.AddCrystals(int.MaxValue);

            Assert.Greater(manager.CrystalTopUpCost("business.vault", 1_000), (long)int.MaxValue);
            Assert.IsFalse(manager.CanAffordCrystalTopUp("business.vault", 1_000));
            Assert.IsFalse(manager.TryBuyWithCrystalTopUp("business.vault", 1_000));
            Assert.AreEqual(int.MaxValue, _ledger.Crystals);
        }

        // ── Save / load ───────────────────────────────────────────────────────────

        [Test]
        public void RoundTrip_ThroughRealJson_KeepsUnitsTheStorageAndTheCoinInProgress()
        {
            _ledger.AddGold(200);
            _manager.TryBuy(Bakery, 2);          // a coin every 30 s
            Elapse(TimeSpan.FromSeconds(95));    // three coins, 5 s into the fourth

            var restored = Reload(_manager.ToDto());
            Assert.AreEqual(2, restored.UnitsOwned(Bakery));
            Assert.AreEqual(3, restored.Stored(Bakery));

            Elapse(TimeSpan.FromSeconds(25));    // the fourth lands for both, or the partial coin was lost
            Assert.AreEqual(4, _manager.Stored(Bakery));
            Assert.AreEqual(4, restored.Stored(Bakery));
        }

        [Test]
        public void Load_SettlesTheTimeAway()
        {
            BuyOneBakery();
            var dto = _manager.ToDto();
            _clock.Advance(TimeSpan.FromHours(2));

            Assert.AreEqual(120, Reload(dto).Stored(Bakery));
        }

        [Test]
        public void Load_AnAbsenceLongerThanTheStorage_StopsAtFull()
        {
            BuyOneBakery();
            var dto = _manager.ToDto();
            _clock.Advance(TimeSpan.FromDays(4));

            Assert.AreEqual(1_440, Reload(dto).Stored(Bakery));
        }

        [Test]
        public void Load_Null_StartsAFreshEconomy()
        {
            _manager.LoadFromDto(null);

            Assert.AreEqual(100, _ledger.Gold);
        }

        [Test]
        public void Load_ABusinessTheSaveDoesNotMention_OwnsNothing_AndGrantsNothing()
        {
            var dto = new EconomyStateDto();
            dto.Businesses[Bakery] = new BusinessStateDto { UnitsOwned = 2, LastUpdatedUtc = _clock.UtcNow.ToString("O") };

            _manager.LoadFromDto(dto);

            Assert.AreEqual(2, _manager.UnitsOwned(Bakery));
            Assert.AreEqual(0, _manager.UnitsOwned(Tavern));
            Assert.AreEqual(0, _ledger.Gold, "Only a missing Economy section grants the starting gold.");
        }

        [Test]
        public void Load_ABusinessThatNoLongerExists_IsSkippedWithAWarning()
        {
            var dto = new EconomyStateDto();
            dto.Businesses["business.mill"] = new BusinessStateDto { UnitsOwned = 7 };
            dto.Businesses[Bakery]          = new BusinessStateDto { UnitsOwned = 1 };

            LogAssert.Expect(LogType.Warning, new Regex("business.mill"));
            _manager.LoadFromDto(dto);

            Assert.AreEqual(1, _manager.UnitsOwned(Bakery));
        }

        [Test]
        public void Load_DamagedNumbers_AreClamped()
        {
            var dto = new EconomyStateDto();
            dto.Businesses[Bakery] = new BusinessStateDto { UnitsOwned = 3, Stored = 999_999_999 };
            dto.Businesses[Tavern] = new BusinessStateDto { UnitsOwned = -5, Stored = -40 };

            _manager.LoadFromDto(dto);

            Assert.AreEqual(4_320, _manager.Stored(Bakery), "Never more than the storage holds.");
            Assert.AreEqual(0, _manager.UnitsOwned(Tavern));
            Assert.AreEqual(0, _manager.Stored(Tavern));

            dto.Businesses[Bakery] = new BusinessStateDto { UnitsOwned = int.MaxValue };
            _manager.LoadFromDto(dto);
            Assert.AreEqual(EconomyConfig.MaxUnitsPerType, _manager.UnitsOwned(Bakery));
        }

        [Test]
        public void Load_AHandEditedFile_StillLoads()
        {
            var json = @"{ ""businesses"": {
                ""business.bakery"": { ""unitsOwned"": 3.0, ""stored"": 1e30 },
                ""business.tavern"": { ""unitsOwned"": 1,   ""stored"": 0, ""lastUpdatedUtc"": ""not a date"" } } }";

            _manager.LoadFromDto(JsonConvert.DeserializeObject<EconomyStateDto>(json));

            Assert.AreEqual(3, _manager.UnitsOwned(Bakery), "A decimal count reads as a whole one.");
            Assert.AreEqual(4_320, _manager.Stored(Bakery), "A huge amount clamps to the storage.");

            // 1,000 an hour: 16 whole coins in a minute. A timestamp read as some old date would
            // instead have filled the whole 24,000-coin storage for free.
            Elapse(TimeSpan.FromMinutes(1));
            Assert.AreEqual(16, _manager.Stored(Tavern), "An unreadable timestamp restarts from now: no gold granted.");
        }

        [Test]
        public void Load_AFutureTimestamp_ResynchronisesInsteadOfStalling()
        {
            var dto = new EconomyStateDto();
            dto.Businesses[Bakery] = new BusinessStateDto
            {
                UnitsOwned = 1, LastUpdatedUtc = _clock.UtcNow.AddYears(1).ToString("O")
            };

            _manager.LoadFromDto(dto);
            Elapse(TimeSpan.FromMinutes(1));

            Assert.AreEqual(1, _manager.Stored(Bakery));
        }

        [Test]
        public void Load_ARetunedSmallerRate_ClampsTheStorageToItsNewSize()
        {
            BuyOneBakery();
            Elapse(TimeSpan.FromHours(24));
            var dto = _manager.ToDto();

            FindAsset(Bakery).GoldPerHour = 30;

            Assert.AreEqual(720, Reload(dto).Stored(Bakery));
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private void Elapse(TimeSpan duration)
        {
            _clock.Advance(duration);
            _clock.Tick();
        }

        private void BuyOneBakery()
        {
            _ledger.AddGold(100);
            Assert.IsTrue(_manager.TryBuy(Bakery, 1));
        }

        /// <summary>A second manager on the same Ledger and clock, loaded from <paramref name="dto"/> through real JSON.</summary>
        private EconomyManager Reload(EconomyStateDto dto)
        {
            var json     = JsonConvert.SerializeObject(dto);
            var restored = new EconomyManager(_ledger, _clock, _config);
            restored.LoadFromDto(JsonConvert.DeserializeObject<EconomyStateDto>(json));
            return restored;
        }

        private BusinessDefinition Business(string id, long price, long perHour)
        {
            var business = ScriptableObject.CreateInstance<BusinessDefinition>();
            business.BusinessId  = id;
            business.UnitPrice   = price;
            business.GoldPerHour = perHour;
            _assets.Add(business);
            return business;
        }

        private EconomyConfig Config(params BusinessDefinition[] businesses)
        {
            var config = ScriptableObject.CreateInstance<EconomyConfig>();
            config.Businesses = businesses;
            _assets.Add(config);
            return config;
        }

        private BusinessDefinition FindAsset(string id)
        {
            foreach (var business in _config.Businesses)
                if (business != null && business.BusinessId == id) return business;
            throw new ArgumentException(id);
        }

        private static string[] IdsOf(IReadOnlyList<BusinessDefinition> businesses)
        {
            var ids = new string[businesses.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = businesses[i].BusinessId;
            return ids;
        }
    }
}
