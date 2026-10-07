using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using KingdomRuler.Systems.Events;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Modules.Trade;
using KingdomRuler.Modules.Trade.Domain;
using KingdomRuler.Modules.Trade.Presenters;
using KingdomRuler.Tests.EditMode.Systems;
using KingdomRuler.Tests.EditMode.Systems.Ledger;

namespace KingdomRuler.Tests.EditMode.Modules.Trade
{
    [TestFixture]
    public sealed class TradePresenterTests
    {
        private FakeClock       _clock;
        private EventBus        _eventBus;
        private KingdomLedger   _ledger;
        private TradeConfig     _config;
        private TradeManager    _manager;
        private FakeAudioService  _audio;
        private FakeHapticService _haptics;
        private FakeLocalizationService _localization;
        private TradeResourceRegistry   _resources;
        private TradePresenter  _presenter;

        private readonly List<UnityEngine.Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            _clock    = new FakeClock();
            _eventBus = new EventBus();
            _ledger   = new KingdomLedger(_eventBus);
            _config   = ScriptableObject.CreateInstance<TradeConfig>();
            _created.Add(_config);

            _manager      = new TradeManager(_ledger, _clock, _config, new System.Random(31337));
            _audio        = new FakeAudioService();
            _haptics      = new FakeHapticService();
            _localization = new FakeLocalizationService();
            _resources    = TestTradeResources.Complete(_created);

            _presenter = new TradePresenter(_manager, _ledger, _eventBus,
                                            _audio, _haptics, _localization, _resources);
            _manager.InitializeNewGame();
        }

        [TearDown]
        public void TearDown()
        {
            _presenter?.Dispose();
            foreach (var obj in _created) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            _created.Clear();
        }

        private void StockUp(float capacity = 20000f)
        {
            foreach (TradeResourceType type in Enum.GetValues(typeof(TradeResourceType)))
            {
                _ledger.SetWarehouseCapacity(type, capacity);
                _ledger.AddTradeResource(type, capacity);
            }
        }

        /// <summary>Make just the receive side of an offer fit, so nothing overflows.</summary>
        private void MakeRoomFor(TradeOffer offer)
        {
            foreach (var pair in offer.ReceiveResources)
                _ledger.SpendTradeResource(pair.Key, pair.Value * 2f);
        }

        // ── Construction ──────────────────────────────────────────────────────────

        [Test]
        public void Constructor_RejectsNullDependencies()
        {
            Assert.Throws<ArgumentNullException>(() => new TradePresenter(
                null, _ledger, _eventBus, _audio, _haptics, _localization, _resources));
            Assert.Throws<ArgumentNullException>(() => new TradePresenter(
                _manager, _ledger, _eventBus, _audio, _haptics, _localization, null));
        }

        // ── Display data ──────────────────────────────────────────────────────────

        [Test]
        public void GetOfferDisplays_ReturnsOneRowPerOffer()
        {
            Assert.AreEqual(_manager.ActiveOffers.Count, _presenter.GetOfferDisplays().Count);
        }

        [Test]
        public void OfferLines_CarryLocalizedNamesFromTheSharedRegistry()
        {
            // Resource names are Ledger-owned, not Trade-owned: Cities and Random Occurrences
            // resolve the identical entries. Assert the exact table:key that was requested.
            var display = _presenter.GetOfferDisplays()[0];
            var line    = display.Give[0];

            string expected = TradeResourceDefinition.StringTable + ":" +
                              TradeResourceDefinition.BuildNameKey(line.Type);

            Assert.AreEqual(expected, line.Name);
            CollectionAssert.Contains(_localization.Requested, expected);
        }

        [Test]
        public void WarehouseDisplay_FillFractionIsClampedToOne()
        {
            StockUp(100f);   // completely full

            var display = _presenter.GetWarehouseDisplay(TradeResourceType.Stone);

            Assert.AreEqual(1f, display.FillFraction, 0.001f);
        }

        [Test]
        public void WarehouseDisplay_NamesThePairedResource()
        {
            var display = _presenter.GetWarehouseDisplay(TradeResourceType.Stone);

            Assert.AreEqual(TradeResourceType.Wood, display.PairedType);
            Assert.IsNotEmpty(display.PairedName);
        }

        [Test]
        public void WarehouseDisplay_AtTheCeiling_ReportsMaxedRatherThanAPrice()
        {
            var dto = new KingdomRuler.Systems.Save.TradeStateDto();
            dto.WarehouseLevels[TradeResourceType.Clay.ToString()] = WarehouseCurve.MaxSupportedLevel;
            _manager.LoadFromDto(dto);

            var display = _presenter.GetWarehouseDisplay(TradeResourceType.Clay);

            Assert.IsFalse(display.CanUpgradeFurther);
            Assert.IsFalse(display.CanAffordCrystalUpgrade);
            StringAssert.Contains(TradeUIText.UpgradeMaxed, display.CrystalCostText);
        }

        // ── GDD §7: never silently disable an offer ───────────────────────────────

        [Test]
        public void AnUnaffordableOffer_StillProducesAConfirmationPanel()
        {
            // GDD §7: "never silently disable the offer". The row stays tappable and the panel
            // explains — so TryGetConfirmation must succeed, not return false.
            var offer = _manager.ActiveOffers[0];    // warehouses are empty, so unaffordable

            Assert.IsTrue(_presenter.TryGetConfirmation(offer.Id, out var display));

            Assert.IsFalse(display.CanAccept);
            Assert.IsNotEmpty(display.BlockerMessages, "The panel must say why.");
        }

        [Test]
        public void ABlockerMessage_NamesTheResourceAtFault()
        {
            var offer = _manager.ActiveOffers[0];

            _presenter.TryGetConfirmation(offer.Id, out var display);

            // Resolved through the keys type, never a re-spelled literal.
            StringAssert.Contains(TradeUIText.BlockerInsufficient, display.BlockerMessages[0]);
        }

        [Test]
        public void AnOverflowingOfferIsStillAcceptable_AndWarnsInsteadOfBlocking()
        {
            // The design rule: a full warehouse is a warning the player may accept, not a refusal.
            StockUp();
            var offer = _manager.ActiveOffers[0];

            Assert.IsTrue(_presenter.TryGetConfirmation(offer.Id, out var display));

            Assert.IsTrue(display.CanAccept, "Overflow must NOT disable Confirm.");
            Assert.IsEmpty(display.BlockerMessages);
            Assert.IsNotEmpty(display.WarningMessages, "The loss must be shown before it happens.");
            StringAssert.Contains(TradeUIText.WarningOverflow, display.WarningMessages[0]);
        }

        [Test]
        public void OfferRow_MarksOverflowWithoutMarkingItUnacceptable()
        {
            StockUp();

            var row = _presenter.GetOfferDisplays()[0];

            Assert.IsTrue(row.HasOverflowWarning);
            Assert.IsTrue(row.CanAccept);
        }

        [Test]
        public void TryGetConfirmation_ForAnUnknownId_ReturnsFalse()
        {
            Assert.IsFalse(_presenter.TryGetConfirmation("nonexistent", out _));
        }

        // ── Price shown == price charged ──────────────────────────────────────────

        [Test]
        public void InstantRefreshCost_IsTheCostCharged()
        {
            _ledger.AddCrystals(100);
            int quoted = _presenter.InstantRefreshCost;

            _presenter.OnInstantRefreshRequested();

            Assert.AreEqual(100 - quoted, _ledger.Crystals);
        }

        [Test]
        public void CrystalUpgradeCost_IsTheCostCharged()
        {
            _ledger.AddCrystals(1000);
            int quoted = _presenter.GetWarehouseDisplay(TradeResourceType.Metal).CrystalUpgradeCost;

            _presenter.OnCrystalUpgradeRequested(TradeResourceType.Metal);

            Assert.AreEqual(1000 - quoted, _ledger.Crystals);
        }

        [Test]
        public void PairedUpgradeCost_IsTheCostCharged()
        {
            var paired = _manager.GetPairedResource(TradeResourceType.Leather);
            _ledger.AddTradeResource(paired, _ledger.GetTradeResource(paired).Capacity);
            float held   = _ledger.GetTradeResource(paired).Amount;
            float quoted = _presenter.GetWarehouseDisplay(TradeResourceType.Leather).PairedUpgradeCost;

            _presenter.OnPairedUpgradeRequested(TradeResourceType.Leather);

            Assert.AreEqual(held - quoted, _ledger.GetTradeResource(paired).Amount, 0.01f);
        }

        // ── Feel hooks ────────────────────────────────────────────────────────────

        [Test]
        public void AcceptingAnOffer_PlaysASoundAndAHaptic()
        {
            StockUp();
            var offer = _manager.ActiveOffers[0];
            MakeRoomFor(offer);

            Assert.IsTrue(_presenter.OnOfferAcceptRequested(offer.Id));

            Assert.IsNotEmpty(_audio.PlayedSfxIds);
            Assert.AreEqual(1, _haptics.LightTriggerCount);
        }

        [Test]
        public void ARefusedOffer_MakesANoiseButDoesNotBuzz()
        {
            // GDD §3 warns against overusing haptics: a rejected action is not a confirmed one.
            var offer = _manager.ActiveOffers[0];   // unaffordable

            Assert.IsFalse(_presenter.OnOfferAcceptRequested(offer.Id));

            Assert.IsNotEmpty(_audio.PlayedSfxIds);
            Assert.AreEqual(0, _haptics.LightTriggerCount);
        }

        [Test]
        public void UpgradingAWarehouse_RaisesOnWarehouseUpgraded()
        {
            _ledger.AddCrystals(1000);
            var upgraded = new List<TradeResourceType>();
            _presenter.OnWarehouseUpgraded += upgraded.Add;

            _presenter.OnCrystalUpgradeRequested(TradeResourceType.Wood);

            CollectionAssert.AreEqual(new[] { TradeResourceType.Wood }, upgraded);
        }

        [Test]
        public void AFailedUpgrade_RaisesNothingAndIsSilent()
        {
            int raised = 0;
            _presenter.OnWarehouseUpgraded += _ => raised++;

            Assert.IsFalse(_presenter.OnCrystalUpgradeRequested(TradeResourceType.Wood));

            Assert.AreEqual(0, raised);
            Assert.IsEmpty(_audio.PlayedSfxIds);
        }

        // ── Screen visibility ─────────────────────────────────────────────────────

        [Test]
        public void SetScreenVisible_ReRendersOnShowOnly()
        {
            int renders = 0;
            _presenter.OnStateChanged += () => renders++;

            _presenter.SetScreenVisible(true);
            Assert.AreEqual(1, renders);

            _presenter.SetScreenVisible(true);              // idempotent
            Assert.AreEqual(1, renders);

            _presenter.SetScreenVisible(false);
            Assert.AreEqual(1, renders, "Hiding does not need a re-render.");
        }

        // ── Subscriptions ─────────────────────────────────────────────────────────

        [Test]
        public void ALocaleChange_TriggersAReRender()
        {
            // Every string on a display struct is a resolved copy that nothing else updates.
            int renders = 0;
            _presenter.OnStateChanged += () => renders++;

            _localization.RaiseLocaleChanged();

            Assert.AreEqual(1, renders);
        }

        [Test]
        public void ACrossModuleResourceChange_TriggersAReRender()
        {
            // A Cities purchase or an occurrence moves a trade resource; the tiles must follow.
            int renders = 0;
            _presenter.OnStateChanged += () => renders++;

            _eventBus.Publish(new ResourceChanged(TradeResourceType.Stone, 10f, 10f));

            Assert.AreEqual(1, renders);
        }

        [Test]
        public void AManagerStateChange_TriggersAReRender()
        {
            _ledger.AddCrystals(1000);
            int renders = 0;
            _presenter.OnStateChanged += () => renders++;

            _manager.UpgradeWarehouseWithCrystals(TradeResourceType.Clay);

            Assert.GreaterOrEqual(renders, 1);
        }

        [Test]
        public void Dispose_UnsubscribesFromAllThreeSources()
        {
            // The Manager, the bus and the localization service all outlive this Presenter, so a
            // missed unsubscribe leaks into the next scene load.
            int renders = 0;
            _presenter.OnStateChanged += () => renders++;

            _presenter.Dispose();
            _presenter = null;

            _localization.RaiseLocaleChanged();
            _eventBus.Publish(new ResourceChanged(TradeResourceType.Wood, 1f, 1f));
            _ledger.AddCrystals(1000);
            _manager.UpgradeWarehouseWithCrystals(TradeResourceType.Wood);

            Assert.AreEqual(0, renders, "A disposed Presenter must be completely silent.");
        }

        // ── Countdown ─────────────────────────────────────────────────────────────

        [Test]
        public void RefreshCountdown_IsResolvedThroughTheKeysType()
        {
            StringAssert.Contains(TradeUIText.RefreshIn, _presenter.GetRefreshCountdownText());
        }
    }
}
