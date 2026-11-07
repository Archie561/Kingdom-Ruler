using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using KingdomRuler.Modules.Trade;
using KingdomRuler.Modules.Trade.UI;
using KingdomRuler.Systems.Events;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Systems.Popups;
using KingdomRuler.Systems.Save;
using KingdomRuler.Tests.EditMode.Systems;
using KingdomRuler.Tests.EditMode.Systems.Ledger;

namespace KingdomRuler.Tests.EditMode.Modules.Trade
{
    /// <summary>
    /// View data, feel hooks and subscriptions. The confirmed purchase paths open popups, which
    /// EditMode cannot answer (no popup registry here, so opening one throws) — they are verified in
    /// Play mode (<c>ARCHITECTURE.md</c> §8), as Laws' buy-up is.
    /// </summary>
    [TestFixture]
    public sealed class TradePresenterTests
    {
        private const TradeResourceType Stone = TradeResourceType.Stone;
        private const TradeResourceType Wood  = TradeResourceType.Wood;
        private const TradeResourceType Clay  = TradeResourceType.Clay;
        private const TradeResourceType Metal = TradeResourceType.Metal;

        private FakeClock               _clock;
        private KingdomLedger           _ledger;
        private TradeConfig             _config;
        private TradeManager            _manager;
        private FakeAudioService        _audio;
        private FakeHapticService       _haptics;
        private FakeLocalizationService _localization;
        private TradeResourceRegistry   _resources;
        private PopupSystem             _popups;
        private TradePresenter          _presenter;

        private readonly List<UnityEngine.Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            _clock   = new FakeClock();
            _ledger  = new KingdomLedger(new EventBus());
            _config  = ScriptableObject.CreateInstance<TradeConfig>();
            _created.Add(_config);
            _manager = new TradeManager(_ledger, _clock, _config, new System.Random(42));

            _audio        = new FakeAudioService();
            _haptics      = new FakeHapticService();
            _localization = new FakeLocalizationService();
            _resources    = TestTradeResources.Complete(_created);

            var popupHost = new GameObject("popups");
            _created.Add(popupHost);
            _popups = popupHost.AddComponent<PopupSystem>();

            _presenter = NewPresenter();
        }

        [TearDown]
        public void TearDown()
        {
            _presenter.Dispose();
            foreach (var obj in _created)
                if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            _created.Clear();
        }

        private TradePresenter NewPresenter() =>
            new TradePresenter(_manager, _ledger, _audio, _haptics, _localization, _resources, _popups);

        private static string Name(TradeResourceType type) => "SharedTable:" + TradeResourceDefinition.BuildNameKey(type);
        private static string Text(string key) => TradeUIText.StringTable + ":" + key;

        /// <summary>A game holding one offer: gives 30 Stone + 10 Wood, receives 30 Clay + 10 Metal.</summary>
        private void LoadStandardOffer()
        {
            _manager.LoadFromDto(new TradeStateDto
            {
                NextOfferRefreshDueUtc = _clock.UtcNow.AddSeconds(600).ToString("O"),
                ActiveOffers = new List<TradeOfferDto>
                {
                    new TradeOfferDto
                    {
                        Id = "o1", Profitability = "Neutral",
                        Give    = new Dictionary<string, int> { ["Stone"] = 30, ["Wood"] = 10 },
                        Receive = new Dictionary<string, int> { ["Clay"] = 30, ["Metal"] = 10 }
                    }
                }
            });
            _ledger.ResetRegenBaseline(_clock.UtcNow);
        }

        // ── Construction ──────────────────────────────────────────────────────────

        [Test]
        public void Constructor_RejectsNullDependencies()
        {
            Assert.Throws<ArgumentNullException>(() => new TradePresenter(null, _ledger, _audio, _haptics, _localization, _resources, _popups));
            Assert.Throws<ArgumentNullException>(() => new TradePresenter(_manager, _ledger, _audio, _haptics, _localization, null, _popups));
            Assert.Throws<ArgumentNullException>(() => new TradePresenter(_manager, _ledger, _audio, _haptics, _localization, _resources, null));
        }

        // ── Tiles ─────────────────────────────────────────────────────────────────

        [Test]
        public void Tile_ShowsStockCapacityAndTheLevelCountingFromOne()
        {
            _manager.InitializeNewGame();
            _ledger.AddTradeResource(Stone, 42);

            var tile = _presenter.GetTile(Stone);

            Assert.AreEqual(42, tile.Stock);
            Assert.AreEqual(100, tile.Capacity);
            Assert.AreEqual(1, tile.DisplayedLevel, "A fresh warehouse is level 1.");
            Assert.AreEqual(0.42f, tile.Fill, 1e-5f);
        }

        [Test]
        public void Tile_FillIsClampedToOne()
        {
            _manager.InitializeNewGame();
            _ledger.AddTradeResource(Stone, 500);

            Assert.AreEqual(1f, _presenter.GetTile(Stone).Fill);
        }

        /// <summary>The view reads tiles every frame; a string on the struct would allocate each time.</summary>
        [Test]
        public void Tile_CarriesNoStrings()
        {
            var fields = typeof(ResourceTileViewData).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            Assert.That(fields.Select(f => f.FieldType), Has.None.EqualTo(typeof(string)));
        }

        [Test]
        public void Tile_ReflectsAnExternalLedgerChangeWithoutAnyEvent()
        {
            // Another mechanic spends Stone: the tile shows it on its next read, with no bus round trip.
            _manager.InitializeNewGame();
            int renders = 0;
            _presenter.StateChanged += () => renders++;

            _ledger.AddTradeResource(Stone, 25);

            Assert.AreEqual(25, _presenter.GetTile(Stone).Stock);
            Assert.AreEqual(0, renders, "Stock is polled; it must not trigger a full re-render.");
        }

        [Test]
        public void ResourceName_ComesFromTheSharedRegistry()
        {
            Assert.AreEqual(Name(Clay), _presenter.ResourceName(Clay));
        }

        // ── Offers ────────────────────────────────────────────────────────────────

        [Test]
        public void GetOffers_ReturnsOneRowPerOffer()
        {
            _manager.InitializeNewGame();

            CollectionAssert.AreEqual(_manager.Offers.Select(o => o.Id), _presenter.GetOffers().Select(r => r.OfferId));
        }

        [Test]
        public void OfferLines_CarryLocalizedNamesFromTheSharedRegistry()
        {
            LoadStandardOffer();

            var row = _presenter.GetOffers().Single();

            Assert.AreEqual(Name(Stone), row.Give[0].Name);
            Assert.AreEqual(30, row.Give[0].Units);
            Assert.AreEqual(Name(Metal), row.Receive[1].Name);
            Assert.AreEqual(10, row.Receive[1].Units);
        }

        // ── The offer confirmation ────────────────────────────────────────────────

        [Test]
        public void OfferPopup_WhenShort_DisablesAcceptAndNamesTheShortfall()
        {
            LoadStandardOffer();
            _ledger.AddTradeResource(Wood, 10);

            var popup = _presenter.GetOfferPopup("o1");

            Assert.IsFalse(popup.CanAccept);
            Assert.AreEqual(Text(TradeUIText.BlockerInsufficient) + "(30," + Name(Stone) + ")", popup.Blockers);
            Assert.IsNull(popup.Warnings);
        }

        [Test]
        public void OfferPopup_WhenAWarehouseOverflows_ListsEveryLossAndKeepsAcceptEnabled()
        {
            LoadStandardOffer();
            _ledger.AddTradeResource(Stone, 30);
            _ledger.AddTradeResource(Wood, 10);
            _ledger.AddTradeResource(Clay, 87);
            _ledger.AddTradeResource(Metal, 95);

            var popup = _presenter.GetOfferPopup("o1");

            Assert.IsTrue(popup.CanAccept, "A full warehouse warns; it does not refuse (GDD §7).");
            Assert.IsNull(popup.Blockers);
            StringAssert.Contains(Text(TradeUIText.OfferOverflow) + "(17," + Name(Clay) + ")", popup.Warnings, "The units lost, not the room left.");
            StringAssert.Contains(Text(TradeUIText.OfferOverflow) + "(5," + Name(Metal) + ")", popup.Warnings);
        }

        [Test]
        public void OfferPopup_ForAMissingOffer_CannotBeAccepted()
        {
            LoadStandardOffer();

            var popup = _presenter.GetOfferPopup("gone");

            Assert.IsFalse(popup.CanAccept);
            Assert.IsEmpty(popup.Give);
        }

        [Test]
        public void OfferPopupData_UnchangedState_ComparesEqual()
        {
            LoadStandardOffer();

            Assert.AreEqual(_presenter.GetOfferPopup("o1"), _presenter.GetOfferPopup("o1"));

            var before = _presenter.GetOfferPopup("o1");
            _ledger.AddTradeResource(Stone, 30);
            Assert.AreNotEqual(before, _presenter.GetOfferPopup("o1"));
        }

        // ── The warehouse popup ───────────────────────────────────────────────────

        [Test]
        public void WarehousePopup_ShowsLevelPlusOne()
        {
            _manager.InitializeNewGame();
            _ledger.AddCrystals(100);
            _manager.TryUpgradeWithCrystals(Stone);

            var popup = _presenter.GetWarehouse(Stone);

            Assert.AreEqual(2, popup.DisplayedLevel);
            Assert.AreEqual(Text(TradeUIText.WarehouseLevel) + "(2)", popup.LevelText);
            Assert.AreEqual(Text(TradeUIText.WarehouseTitle) + "(" + Name(Stone) + ")", popup.Title);
        }

        [Test]
        public void WarehousePopup_PricesAreTheManagers_AndThePairedOneNamesThePairedResource()
        {
            _manager.InitializeNewGame();

            var popup = _presenter.GetWarehouse(Stone);

            Assert.AreEqual(Text(TradeUIText.UpgradeCostCrystals) + "(" + _manager.CrystalUpgradeCost(Stone) + ")", popup.CrystalPrice);
            Assert.AreEqual(Text(TradeUIText.UpgradeCostPaired) + "(80," + Name(Wood) + ")", popup.PairedPrice);
            Assert.IsTrue(popup.CanUpgrade);
            Assert.IsNull(popup.MaxedText);
        }

        [Test]
        public void WarehousePopup_AtTheCap_ShowsFullyUpgraded()
        {
            _manager.LoadFromDto(new TradeStateDto
            {
                WarehouseLevels = new Dictionary<string, int> { ["Clay"] = WarehouseCurve.TopLevel(_config) }
            });

            var popup = _presenter.GetWarehouse(Clay);

            Assert.AreEqual(_config.MaxWarehouseLevel, popup.DisplayedLevel, "The top level is shown as the configured cap.");
            Assert.IsFalse(popup.CanUpgrade);
            Assert.AreEqual(Text(TradeUIText.UpgradeMaxed), popup.MaxedText);
            Assert.IsNull(popup.CrystalPrice);
        }

        [Test]
        public void WarehousePopup_ShowsRateAndTimeUntilFull()
        {
            _manager.InitializeNewGame();
            _clock.Advance(TimeSpan.FromSeconds(432));   // half a unit at capacity 100

            var popup = _presenter.GetWarehouse(Stone);

            Assert.AreEqual(Text(TradeUIText.WarehouseRate) + "(4)", popup.RateText);
            Assert.AreEqual(Text(TradeUIText.WarehouseFullIn) + "(23:52:48)", popup.FullText);
            Assert.IsTrue(popup.ShowsNextUnit);
            Assert.AreEqual(50, popup.NextUnitPercent);
        }

        [Test]
        public void WarehousePopup_WhenFull_ShowsFullAndHidesTheNextUnitBar()
        {
            _manager.InitializeNewGame();
            _ledger.AddTradeResource(Stone, 100);

            var popup = _presenter.GetWarehouse(Stone);

            Assert.AreEqual(Text(TradeUIText.WarehouseFull), popup.FullText);
            Assert.IsFalse(popup.ShowsNextUnit);
        }

        [Test]
        public void WarehousePopupData_UnchangedState_ComparesEqual()
        {
            _manager.InitializeNewGame();

            Assert.AreEqual(_presenter.GetWarehouse(Stone), _presenter.GetWarehouse(Stone));

            var before = _presenter.GetWarehouse(Stone);
            _ledger.AddTradeResource(Stone, 1);
            Assert.AreNotEqual(before, _presenter.GetWarehouse(Stone));
        }

        // ── Refresh ───────────────────────────────────────────────────────────────

        [Test]
        public void RefreshPrice_IsTheManagersPrice()
        {
            Assert.AreEqual(_manager.InstantRefreshCost, _presenter.InstantRefreshCost);
        }

        [Test]
        public void RefreshCountdown_IsResolvedThroughTheKeysType()
        {
            Assert.AreEqual(Text(TradeUIText.RefreshIn) + "(20:00)", _presenter.RefreshCountdownText(1200));
        }

        [Test]
        public void AutoRefresh_OnAVisibleScreen_PlaysTheSoundWithoutAHaptic()
        {
            _manager.InitializeNewGame();
            _presenter.SetScreenVisible(true);
            _audio.PlayedSfxIds.Clear();

            _clock.Advance(TimeSpan.FromSeconds(_config.OfferRefreshTimeSeconds));
            _clock.Tick();

            CollectionAssert.AreEqual(new[] { "sfx_trade_offers_refreshed" }, _audio.PlayedSfxIds);
            Assert.AreEqual(0, _haptics.LightTriggerCount, "A passive refresh is not a player action.");
        }

        [Test]
        public void AutoRefresh_OnAHiddenScreen_IsSilent_ButStillReRenders()
        {
            _manager.InitializeNewGame();
            _presenter.SetScreenVisible(false);
            _audio.PlayedSfxIds.Clear();
            int renders = 0;
            _presenter.StateChanged += () => renders++;

            _clock.Advance(TimeSpan.FromSeconds(_config.OfferRefreshTimeSeconds));
            _clock.Tick();

            Assert.IsEmpty(_audio.PlayedSfxIds);
            Assert.AreEqual(1, renders);
        }

        [Test]
        public void OffersArrived_OnRefreshButNotOnAccept()
        {
            LoadStandardOffer();
            _ledger.AddTradeResource(Stone, 30);
            _ledger.AddTradeResource(Wood, 10);
            int arrivals = 0;
            _presenter.OffersArrived += () => arrivals++;

            _manager.AcceptOffer("o1");
            Assert.AreEqual(0, arrivals, "An accept removes a row; nothing new arrives.");

            _clock.Advance(TimeSpan.FromSeconds(600));
            _clock.Tick();
            Assert.AreEqual(1, arrivals);
        }

        // ── Subscriptions and visibility ──────────────────────────────────────────

        [Test]
        public void OffersChanged_TriggersAReRender()
        {
            int renders = 0;
            _presenter.StateChanged += () => renders++;

            _manager.InitializeNewGame();

            Assert.AreEqual(1, renders);
        }

        [Test]
        public void SetScreenVisible_ReRendersOnShowOnly()
        {
            int renders = 0;
            _presenter.StateChanged += () => renders++;

            _presenter.SetScreenVisible(true);
            _presenter.SetScreenVisible(true);
            _presenter.SetScreenVisible(false);

            Assert.AreEqual(1, renders);
        }

        [Test]
        public void ALocaleChange_TriggersAReRender()
        {
            int renders = 0;
            _presenter.StateChanged += () => renders++;

            _localization.RaiseLocaleChanged();

            Assert.AreEqual(1, renders);
        }

        [Test]
        public void Dispose_StopsListening()
        {
            int renders = 0;
            _presenter.StateChanged += () => renders++;

            _presenter.Dispose();
            _manager.InitializeNewGame();
            _localization.RaiseLocaleChanged();

            Assert.AreEqual(0, renders);
        }

        // ── Opening a popup never spends ──────────────────────────────────────────

        [Test]
        public void OpeningAnOffer_SpendsNothing()
        {
            LoadStandardOffer();
            _ledger.AddTradeResource(Stone, 30);
            _ledger.AddTradeResource(Wood, 10);

            LogAssert.Expect(LogType.Exception, new Regex("No popup registry"));
            _presenter.OpenOffer("o1").Forget();

            Assert.AreEqual(30, _ledger.GetTradeResource(Stone).Amount);
            Assert.AreEqual(1, _manager.Offers.Count, "A popup that could not be shown must never fall through into the trade.");
        }

        [Test]
        public void OpeningAWarehouse_SpendsNothing()
        {
            _manager.InitializeNewGame();
            _ledger.AddCrystals(100);

            LogAssert.Expect(LogType.Exception, new Regex("No popup registry"));
            _presenter.OpenWarehouse(Stone).Forget();

            Assert.AreEqual(100, _ledger.Crystals);
            Assert.AreEqual(0, _manager.WarehouseLevel(Stone));
        }

        [Test]
        public void OpeningTheRefresh_SpendsNothing()
        {
            _manager.InitializeNewGame();
            _ledger.AddCrystals(100);
            var before = _manager.Offers.Select(o => o.Id).ToList();

            LogAssert.Expect(LogType.Exception, new Regex("No popup registry"));
            _presenter.RequestInstantRefresh().Forget();

            Assert.AreEqual(100, _ledger.Crystals);
            CollectionAssert.AreEqual(before, _manager.Offers.Select(o => o.Id));
        }
    }
}
