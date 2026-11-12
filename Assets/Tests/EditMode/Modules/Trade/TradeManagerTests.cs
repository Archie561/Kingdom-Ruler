using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using KingdomRuler.Modules.Trade;
using KingdomRuler.Systems.Events;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Systems.Save;
using KingdomRuler.Tests.EditMode.Systems;

namespace KingdomRuler.Tests.EditMode.Modules.Trade
{
    [TestFixture]
    public sealed class TradeManagerTests
    {
        private const TradeResourceType Stone = TradeResourceType.Stone;
        private const TradeResourceType Wood  = TradeResourceType.Wood;
        private const TradeResourceType Clay  = TradeResourceType.Clay;
        private const TradeResourceType Metal = TradeResourceType.Metal;

        private static readonly TradeResourceType[] AllResources =
            (TradeResourceType[])Enum.GetValues(typeof(TradeResourceType));

        private FakeClock     _clock;
        private KingdomLedger _ledger;
        private TradeConfig   _config;
        private TradeManager  _manager;

        [SetUp]
        public void SetUp()
        {
            _clock   = new FakeClock();
            _ledger  = new KingdomLedger(new EventBus());
            _config  = ScriptableObject.CreateInstance<TradeConfig>();
            _manager = NewManager();
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(_config);

        private TradeManager NewManager(int seed = 42) => new TradeManager(_ledger, _clock, _config, new System.Random(seed));

        private void Wait(double seconds)
        {
            _clock.Advance(TimeSpan.FromSeconds(seconds));
            _manager.AdvanceTo(_clock.UtcNow);
        }

        private int Stock(TradeResourceType type) => _ledger.GetTradeResource(type).Amount;

        // ── Fixture offers ────────────────────────────────────────────────────────

        private static Dictionary<string, int> Units(params (TradeResourceType type, int units)[] lines) =>
            lines.ToDictionary(l => l.type.ToString(), l => l.units);

        /// <summary>Gives 30 Stone + 10 Wood; receives 30 Clay + 10 Metal.</summary>
        private static TradeOfferDto StandardOffer(string id = "o1") => new TradeOfferDto
        {
            Id = id, Profitability = "Neutral",
            Give    = Units((Stone, 30), (Wood, 10)),
            Receive = Units((Clay, 30), (Metal, 10))
        };

        private TradeStateDto StateWith(params TradeOfferDto[] offers) => new TradeStateDto
        {
            NextOfferRefreshDueUtc = _clock.UtcNow.AddSeconds(600).ToString("O"),
            WarehouseLevels        = new Dictionary<string, int>(),
            ActiveOffers           = offers.ToList()
        };

        /// <summary>A game holding exactly the given offers, with base warehouses and a regen baseline at now.</summary>
        private void LoadOffers(params TradeOfferDto[] offers)
        {
            _manager.LoadFromDto(StateWith(offers));
            _ledger.ResetRegenBaseline(_clock.UtcNow);
        }

        private void PayFor(TradeOfferDto offer)
        {
            foreach (var line in offer.Give) _ledger.AddTradeResource((TradeResourceType)Enum.Parse(typeof(TradeResourceType), line.Key), line.Value);
        }

        // ── Construction and a new game ───────────────────────────────────────────

        [Test]
        public void Constructor_GeneratesNoOffers()
        {
            Assert.IsEmpty(_manager.Offers, "Offers come from InitializeNewGame or LoadFromDto, never from construction.");
        }

        [Test]
        public void InitializeNewGame_FillsTheListAndStartsTheTimer()
        {
            _manager.InitializeNewGame();

            Assert.AreEqual(_config.OfferCount, _manager.Offers.Count);
            Assert.AreEqual(_config.OfferRefreshTimeSeconds, _manager.SecondsUntilRefresh, 0.01f);
        }

        [Test]
        public void InitializeNewGame_SetsBaseCapacity_AndAnEmptyWarehouseFillsInADay()
        {
            _manager.InitializeNewGame();

            foreach (var resource in AllResources)
                Assert.AreEqual(100, _ledger.GetTradeResource(resource).Capacity);

            _ledger.AccruePassiveResourceRegen(_clock.UtcNow.AddDays(1));
            Assert.AreEqual(100, Stock(Stone));
        }

        // ── Refresh ───────────────────────────────────────────────────────────────

        [Test]
        public void AdvanceTo_BeforeTheDeadline_KeepsTheSameOffers()
        {
            _manager.InitializeNewGame();
            var before = _manager.Offers.Select(o => o.Id).ToList();

            Wait(_config.OfferRefreshTimeSeconds - 1);

            CollectionAssert.AreEqual(before, _manager.Offers.Select(o => o.Id));
        }

        [Test]
        public void AdvanceTo_AfterTheDeadline_ReplacesTheOffers()
        {
            _manager.InitializeNewGame();
            var before = _manager.Offers.Select(o => o.Id).ToList();

            Wait(_config.OfferRefreshTimeSeconds);

            CollectionAssert.IsEmpty(_manager.Offers.Select(o => o.Id).Intersect(before));
            Assert.AreEqual(_config.OfferCount, _manager.Offers.Count);
        }

        [Test]
        public void AdvanceTo_RaisesOffersChangedOnlyWhenTheListIsReplaced()
        {
            _manager.InitializeNewGame();
            int changes = 0;
            _manager.OffersChanged += () => changes++;

            Wait(10);
            Assert.AreEqual(0, changes);

            Wait(_config.OfferRefreshTimeSeconds);
            Assert.AreEqual(1, changes);
        }

        [Test]
        public void AClockTick_RefreshesTheOffersOnceTheDeadlinePasses()
        {
            _manager.InitializeNewGame();
            var before = _manager.Offers[0].Id;

            _clock.Advance(TimeSpan.FromSeconds(_config.OfferRefreshTimeSeconds));
            _clock.Tick();

            Assert.AreNotEqual(before, _manager.Offers[0].Id, "The manager subscribes itself to the clock.");
        }

        [Test]
        public void Refresh_SizesOffersFromCurrentCapacities()
        {
            var state = StateWith();
            foreach (var resource in AllResources) state.WarehouseLevels[resource.ToString()] = 10;
            _manager.LoadFromDto(state);

            Wait(_config.OfferRefreshTimeSeconds);

            // Level 10 holds 5,770: the smallest offer gives 0.20 × 2 × 5,770 = 2,308.
            Assert.That(_manager.Offers.Select(o => o.TotalGive), Is.All.GreaterThanOrEqualTo(2308));
        }

        [Test]
        public void AnUpgrade_DoesNotResizeOffersAlreadyListed()
        {
            _manager.InitializeNewGame();
            var before = _manager.Offers.Select(o => o.TotalGive).ToList();

            _ledger.AddCrystals(10_000);
            foreach (var resource in AllResources)
                for (int i = 0; i < 5; i++) _manager.TryUpgradeWithCrystals(resource);

            CollectionAssert.AreEqual(before, _manager.Offers.Select(o => o.TotalGive));
        }

        [Test]
        public void AdvanceTo_LeavesRegenToTheLedger()
        {
            _manager.InitializeNewGame();

            Wait(3600);

            Assert.AreEqual(0, Stock(Stone), "Regen is the Ledger's, settled on its own tick.");
        }

        // ── Instant refresh ───────────────────────────────────────────────────────

        [Test]
        public void TryInstantRefresh_SpendsCrystalsAndResetsTheTimer()
        {
            _manager.InitializeNewGame();
            Wait(600);
            _ledger.AddCrystals(_manager.InstantRefreshCost + 2);
            var before = _manager.Offers[0].Id;

            Assert.IsTrue(_manager.TryInstantRefresh());

            Assert.AreEqual(2, _ledger.Crystals);
            Assert.AreNotEqual(before, _manager.Offers[0].Id);
            Assert.AreEqual(_config.OfferRefreshTimeSeconds, _manager.SecondsUntilRefresh, 0.01f);
        }

        [Test]
        public void TryInstantRefresh_WithoutEnoughCrystals_ChangesNothing()
        {
            _manager.InitializeNewGame();
            _ledger.AddCrystals(_manager.InstantRefreshCost - 1);
            var before = _manager.Offers.Select(o => o.Id).ToList();

            Assert.IsFalse(_manager.TryInstantRefresh());

            Assert.AreEqual(_manager.InstantRefreshCost - 1, _ledger.Crystals);
            CollectionAssert.AreEqual(before, _manager.Offers.Select(o => o.Id));
        }

        [Test]
        public void CanAffordInstantRefresh_MatchesWhetherTheChargeSucceeds()
        {
            _manager.InitializeNewGame();
            for (int crystals = 0; crystals <= _manager.InstantRefreshCost + 1; crystals++)
            {
                _ledger.SetCrystals(crystals);
                bool quoted = _manager.CanAffordInstantRefresh;
                Assert.AreEqual(quoted, _manager.TryInstantRefresh(), $"With {crystals} crystals.");
            }
        }

        // ── Evaluate ──────────────────────────────────────────────────────────────

        [Test]
        public void Evaluate_UnknownId_ReportsUnavailable()
        {
            LoadOffers(StandardOffer());

            var evaluation = _manager.Evaluate("nope");

            Assert.IsFalse(evaluation.OfferExists);
            Assert.IsFalse(evaluation.CanAccept);
            Assert.AreEqual(TradeIssueKind.OfferUnavailable, evaluation.Issues[0].Kind);
        }

        [Test]
        public void Evaluate_WhenShortOnAGiveResource_NamesThatResourceAndTheShortfall()
        {
            LoadOffers(StandardOffer());
            _ledger.AddTradeResource(Stone, 18);
            _ledger.AddTradeResource(Wood, 10);

            var evaluation = _manager.Evaluate("o1");

            Assert.IsFalse(evaluation.CanAccept);
            var issue = evaluation.Issues.Single();
            Assert.AreEqual(TradeIssueKind.InsufficientResource, issue.Kind);
            Assert.AreEqual(Stone, issue.Resource);
            Assert.AreEqual(12, issue.Units);
        }

        [Test]
        public void Evaluate_ListsEveryIssueAtOnce()
        {
            LoadOffers(StandardOffer());
            _ledger.AddTradeResource(Clay, 95);

            var evaluation = _manager.Evaluate("o1");

            // Short of both Stone and Wood, and Clay overflows: three messages, not the first one.
            Assert.AreEqual(3, evaluation.Issues.Count);
        }

        [Test]
        public void Evaluate_MutatesNothing()
        {
            LoadOffers(StandardOffer());
            PayFor(StandardOffer());

            _manager.Evaluate("o1");

            Assert.AreEqual(30, Stock(Stone));
            Assert.AreEqual(1, _manager.Offers.Count);
        }

        [Test]
        public void Evaluate_WhenAWarehouseIsFull_WarnsWithTheExcess_ButStaysAcceptable()
        {
            LoadOffers(StandardOffer());
            PayFor(StandardOffer());
            _ledger.AddTradeResource(Clay, 87);

            var evaluation = _manager.Evaluate("o1");

            Assert.IsTrue(evaluation.CanAccept, "A full warehouse warns; it does not refuse (GDD §7).");
            var warning = evaluation.Issues.Single();
            Assert.AreEqual(TradeIssueKind.WouldOverflowWarehouse, warning.Kind);
            Assert.AreEqual(Clay, warning.Resource);
            Assert.AreEqual(17, warning.Units, "30 offered, room for 13: 17 will be lost.");
        }

        // ── Accept ────────────────────────────────────────────────────────────────

        [Test]
        public void AcceptOffer_MovesResourcesAndRemovesTheOffer()
        {
            LoadOffers(StandardOffer());
            PayFor(StandardOffer());

            var result = _manager.AcceptOffer("o1");

            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(0, Stock(Stone));
            Assert.AreEqual(0, Stock(Wood));
            Assert.AreEqual(30, Stock(Clay));
            Assert.AreEqual(10, Stock(Metal));
            Assert.IsEmpty(_manager.Offers);
        }

        [Test]
        public void AcceptOffer_ConsumesTheOfferWithoutReplacingIt_OrMovingTheDeadline()
        {
            LoadOffers(StandardOffer(), StandardOffer("o2"));
            PayFor(StandardOffer());
            var deadline = _manager.NextRefreshDueUtc;

            _manager.AcceptOffer("o1");

            Assert.AreEqual(1, _manager.Offers.Count);
            Assert.AreEqual(deadline, _manager.NextRefreshDueUtc, "Only a refresh moves the deadline.");
        }

        [Test]
        public void AcceptOffer_WhenShort_IsRefusedAndSpendsNothing()
        {
            LoadOffers(StandardOffer());
            _ledger.AddTradeResource(Stone, 30);   // Wood is missing

            var result = _manager.AcceptOffer("o1");

            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(30, Stock(Stone));
            Assert.AreEqual(0, Stock(Clay));
            Assert.AreEqual(1, _manager.Offers.Count);
        }

        [Test]
        public void AcceptOffer_WithAStaleId_IsRefusedAndTouchesNothing()
        {
            LoadOffers(StandardOffer());
            PayFor(StandardOffer());

            var result = _manager.AcceptOffer("refreshed-away");

            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(TradeIssueKind.OfferUnavailable, result.Issues[0].Kind);
            Assert.AreEqual(30, Stock(Stone));
        }

        [Test]
        public void OffersChanged_FiresOncePerAccept()
        {
            LoadOffers(StandardOffer());
            PayFor(StandardOffer());
            int changes = 0;
            _manager.OffersChanged += () => changes++;

            _manager.AcceptOffer("o1");

            Assert.AreEqual(1, changes);
        }

        [Test]
        public void AcceptOffer_AccruesRegenFirst_SoAJustCrossedThresholdIsHonoured()
        {
            LoadOffers(StandardOffer());
            _ledger.AddTradeResource(Stone, 29);
            _ledger.AddTradeResource(Wood, 10);
            _clock.Advance(TimeSpan.FromSeconds(864));   // one unit at capacity 100, not yet settled

            Assert.IsFalse(_manager.Evaluate("o1").CanAccept, "Unsettled, Stone is one short.");
            Assert.IsTrue(_manager.AcceptOffer("o1").Accepted);
        }

        [Test]
        public void Accept_IntoANearlyFullWarehouse_ReportsReceivedAndForfeited()
        {
            LoadOffers(StandardOffer());
            PayFor(StandardOffer());
            _ledger.AddTradeResource(Clay, 87);

            var result = _manager.AcceptOffer("o1");

            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(100, Stock(Clay), "Exactly full, never one short.");
            Assert.AreEqual(13, result.Received.Single(a => a.Resource == Clay).Units);
            Assert.AreEqual(10, result.Received.Single(a => a.Resource == Metal).Units);
            var lost = result.Forfeited.Single();
            Assert.AreEqual(Clay, lost.Resource);
            Assert.AreEqual(17, lost.Units);
        }

        [Test]
        public void Accept_ForfeitedEqualsTheWarnedExcess()
        {
            LoadOffers(StandardOffer());
            PayFor(StandardOffer());
            _ledger.AddTradeResource(Clay, 75);
            _ledger.AddTradeResource(Metal, 96);

            var warned = _manager.Evaluate("o1").Issues.ToDictionary(i => i.Resource, i => i.Units);
            var lost   = _manager.AcceptOffer("o1").Forfeited.ToDictionary(a => a.Resource, a => a.Units);

            CollectionAssert.AreEquivalent(warned, lost);
        }

        [Test]
        public void AcceptOffer_WithRoomToSpare_ForfeitsNothing()
        {
            LoadOffers(StandardOffer());
            PayFor(StandardOffer());

            var result = _manager.AcceptOffer("o1");

            Assert.IsEmpty(result.Forfeited);
            Assert.AreEqual(40, result.Received.Sum(a => a.Units));
        }

        // ── Warehouse upgrades ────────────────────────────────────────────────────

        [Test]
        public void TryUpgradeWithCrystals_RaisesTheLevelAndTheCapacity()
        {
            _manager.InitializeNewGame();
            _ledger.AddCrystals(100);
            int expectedCapacity = _manager.CapacityAfterUpgrade(Stone);

            Assert.IsTrue(_manager.TryUpgradeWithCrystals(Stone));

            Assert.AreEqual(1, _manager.WarehouseLevel(Stone));
            Assert.AreEqual(150, expectedCapacity);
            Assert.AreEqual(expectedCapacity, _ledger.GetTradeResource(Stone).Capacity);
        }

        [Test]
        public void CrystalUpgrade_QuotedEqualsCharged_AtEveryLevel()
        {
            _manager.InitializeNewGame();
            for (int level = 0; level < WarehouseCurve.TopLevel(_config); level++)
            {
                int quote = _manager.CrystalUpgradeCost(Stone);
                _ledger.SetCrystals(quote);

                Assert.IsTrue(_manager.TryUpgradeWithCrystals(Stone), $"Level {level}.");
                Assert.AreEqual(0, _ledger.Crystals, $"Level {level}: charged differently from the quote {quote}.");
            }
        }

        [Test]
        public void TryUpgradeWithCrystals_WithoutEnough_ChangesNothing()
        {
            _manager.InitializeNewGame();
            _ledger.AddCrystals(_manager.CrystalUpgradeCost(Stone) - 1);

            Assert.IsFalse(_manager.TryUpgradeWithCrystals(Stone));
            Assert.AreEqual(0, _manager.WarehouseLevel(Stone));
            Assert.AreEqual(100, _ledger.GetTradeResource(Stone).Capacity);
        }

        [Test]
        public void TryUpgradeWithPairedResource_Costs80PercentOfItsOwnCapacity_TakenFromPairedStock()
        {
            _manager.InitializeNewGame();
            _ledger.AddTradeResource(Wood, 95);

            Assert.AreEqual(80, _manager.PairedUpgradeCost(Stone), "80% of Stone's capacity of 100, paid in Wood.");
            Assert.IsTrue(_manager.TryUpgradeWithPairedResource(Stone));

            Assert.AreEqual(15, Stock(Wood));
            Assert.AreEqual(1, _manager.WarehouseLevel(Stone));
        }

        [Test]
        public void PairedUpgradeCost_RisesWithTheWarehouseBeingUpgraded_NotWithItsPair()
        {
            _manager.InitializeNewGame();
            _ledger.AddCrystals(_manager.CrystalUpgradeCost(Stone));
            _manager.TryUpgradeWithCrystals(Stone);   // Stone 150, Wood still 100

            Assert.AreEqual(120, _manager.PairedUpgradeCost(Stone), "80% of Stone's new 150.");
            Assert.AreEqual(80,  _manager.PairedUpgradeCost(Wood),  "Wood's price follows Wood, not Stone.");
        }

        [Test]
        public void APairedUpgrade_CannotTakeAWarehouseMoreThanOneLevelAheadOfItsPair()
        {
            // The pair's stock is capped by its capacity: 80% of a warehouse 1.5× the pair's size
            // is more than the pair can ever hold.
            _manager.InitializeNewGame();
            _ledger.AddTradeResource(Wood, 100);
            Assert.IsTrue(_manager.TryUpgradeWithPairedResource(Stone), "Level for level: affordable.");

            _ledger.AddTradeResource(Wood, 100);   // Wood full again
            Assert.AreEqual(120, _manager.PairedUpgradeCost(Stone));
            Assert.IsFalse(_manager.CanAffordPairedUpgrade(Stone), "120 Wood, from a warehouse of 100.");
            Assert.IsFalse(_manager.TryUpgradeWithPairedResource(Stone));
            Assert.AreEqual(1, _manager.WarehouseLevel(Stone));

            // Wood catching up unblocks Stone again.
            _ledger.AddTradeResource(Stone, _ledger.GetTradeResource(Stone).Capacity);
            Assert.IsTrue(_manager.TryUpgradeWithPairedResource(Wood), "Wood catches up, paying 80 Stone.");
            _ledger.AddTradeResource(Wood, _ledger.GetTradeResource(Wood).Capacity);
            Assert.IsTrue(_manager.CanAffordPairedUpgrade(Stone), "120 Wood, from a warehouse now of 150.");
        }

        [Test]
        public void PairedUpgrade_QuotedEqualsCharged_AtEveryLevel()
        {
            // Stone and Wood leapfrog, so each pays 80% of a warehouse that keeps growing.
            _manager.InitializeNewGame();
            for (int step = 0; step < WarehouseCurve.TopLevel(_config); step++)
                foreach (var type in new[] { Stone, Wood })
                {
                    var paired = TradeResourcePair.PairedWith(type);
                    _ledger.AddTradeResource(paired, _ledger.GetTradeResource(paired).Capacity);
                    int quote  = _manager.PairedUpgradeCost(type);
                    int before = Stock(paired);

                    Assert.IsTrue(_manager.TryUpgradeWithPairedResource(type), $"{type}, step {step}.");
                    Assert.AreEqual(quote, before - Stock(paired), $"{type}, step {step}: charged differently from the quote.");
                }
        }

        [Test]
        public void PairedUpgradeCost_RoundsUp()
        {
            _config.CapacityRoundTo = 1f;
            _config.CapacityBase    = 101f;
            _manager.InitializeNewGame();

            Assert.AreEqual(81, _manager.PairedUpgradeCost(Stone), "80% of 101 is 80.8, never charged as 80.");
        }

        [Test]
        public void TryUpgradeWithPairedResource_AccruesRegenFirst()
        {
            _manager.InitializeNewGame();
            _ledger.AddTradeResource(Wood, 79);
            _clock.Advance(TimeSpan.FromSeconds(864));   // the 80th unit, not yet settled

            Assert.IsFalse(_manager.CanAffordPairedUpgrade(Stone));
            Assert.IsTrue(_manager.TryUpgradeWithPairedResource(Stone));
        }

        [Test]
        public void TryUpgradeWithPairedResource_DoesNotChangeThePairedWarehousesOwnCapacity()
        {
            _manager.InitializeNewGame();
            _ledger.AddTradeResource(Wood, 100);

            _manager.TryUpgradeWithPairedResource(Stone);

            Assert.AreEqual(100, _ledger.GetTradeResource(Wood).Capacity);
            Assert.AreEqual(0, _manager.WarehouseLevel(Wood));
        }

        [Test]
        public void TryUpgradeWithPairedResource_WithoutEnoughStock_ChangesNothing()
        {
            _manager.InitializeNewGame();
            _ledger.AddTradeResource(Wood, 79);

            Assert.IsFalse(_manager.TryUpgradeWithPairedResource(Stone));
            Assert.AreEqual(79, Stock(Wood));
            Assert.AreEqual(0, _manager.WarehouseLevel(Stone));
        }

        [Test]
        public void CanAffordUpgrades_MatchWhetherTheChargeSucceeds()
        {
            // The first crystal upgrade costs 5; the first paired one 80 Wood.
            foreach (int crystals in new[] { 0, 4, 5, 6 })
            {
                var manager = FreshGame(out var ledger);
                ledger.SetCrystals(crystals);
                Assert.AreEqual(manager.CanAffordCrystalUpgrade(Clay), manager.TryUpgradeWithCrystals(Clay), $"{crystals} crystals.");
            }

            foreach (int wood in new[] { 0, 79, 80, 81 })
            {
                var manager = FreshGame(out var ledger);
                ledger.AddTradeResource(Wood, wood);
                Assert.AreEqual(manager.CanAffordPairedUpgrade(Stone), manager.TryUpgradeWithPairedResource(Stone), $"{wood} Wood.");
            }
        }

        private TradeManager FreshGame(out KingdomLedger ledger)
        {
            ledger = new KingdomLedger(new EventBus());
            var manager = new TradeManager(ledger, _clock, _config, new System.Random(1));
            manager.InitializeNewGame();
            return manager;
        }

        [Test]
        public void AfterAnUpgrade_AnEmptyWarehouseStillFillsInADay()
        {
            _manager.InitializeNewGame();
            _ledger.AddCrystals(1000);
            _manager.TryUpgradeWithCrystals(Stone);
            _manager.TryUpgradeWithCrystals(Stone);

            _ledger.AccruePassiveResourceRegen(_clock.UtcNow.AddDays(1));

            Assert.AreEqual(220, Stock(Stone));
        }

        [Test]
        public void UpgradesStopAtMaxWarehouseLevel()
        {
            var state = StateWith();
            state.WarehouseLevels[Stone.ToString()] = WarehouseCurve.TopLevel(_config);
            _manager.LoadFromDto(state);
            _ledger.AddCrystals(int.MaxValue / 2);
            _ledger.AddTradeResource(Wood, 100);

            Assert.IsFalse(_manager.CanUpgradeFurther(Stone));
            Assert.IsFalse(_manager.CanAffordCrystalUpgrade(Stone));
            Assert.IsFalse(_manager.CanAffordPairedUpgrade(Stone));
            Assert.IsFalse(_manager.TryUpgradeWithCrystals(Stone));
            Assert.IsFalse(_manager.TryUpgradeWithPairedResource(Stone));
            Assert.AreEqual(WarehouseCurve.TopLevel(_config), _manager.WarehouseLevel(Stone));
            Assert.AreEqual(_ledger.GetTradeResource(Stone).Capacity, _manager.CapacityAfterUpgrade(Stone));
        }

        // ── Regeneration read-outs ────────────────────────────────────────────────

        [Test]
        public void RegenReadouts_AreTheLedgersAtTheClocksNow()
        {
            _manager.InitializeNewGame();
            _clock.Advance(TimeSpan.FromSeconds(432));   // half a unit at capacity 100

            Assert.AreEqual(0.5f, _manager.NextUnitProgress(Stone), 1e-5f);
            Assert.AreEqual(864f, _manager.SecondsPerUnit(Stone), 1e-3f);
            Assert.AreEqual(86400f - 432f, _manager.SecondsUntilFull(Stone), 1e-2f);
        }

        [Test]
        public void UnitsPerHour_IsCapacityOver24RoundedDown()
        {
            _manager.InitializeNewGame();
            Assert.AreEqual(4, _manager.UnitsPerHour(Stone), "100 a day is 4.17 an hour.");

            _ledger.AddCrystals(1000);
            for (int i = 0; i < 5; i++) _manager.TryUpgradeWithCrystals(Stone);
            Assert.AreEqual(31, _manager.UnitsPerHour(Stone), "760 a day is 31.7 an hour.");
        }

        // ── Save / load ───────────────────────────────────────────────────────────

        private TradeManager Reloaded()
        {
            var dto = _manager.ToDto();
            var manager = new TradeManager(_ledger, _clock, _config, new System.Random(999));
            manager.LoadFromDto(dto);
            return manager;
        }

        [Test]
        public void RelaunchWithoutTimePassing_DoesNotRerollTheOffers()
        {
            _manager.InitializeNewGame();

            CollectionAssert.AreEqual(_manager.Offers.Select(o => o.Id), Reloaded().Offers.Select(o => o.Id));
        }

        [Test]
        public void SaveLoad_RestoresOfferTermsExactly()
        {
            _manager.InitializeNewGame();
            var restored = Reloaded();

            for (int i = 0; i < _manager.Offers.Count; i++)
            {
                var a = _manager.Offers[i];
                var b = restored.Offers[i];
                Assert.AreEqual(a.Profitability, b.Profitability);
                CollectionAssert.AreEqual(a.Give.Select(x => (x.Resource, x.Units)), b.Give.Select(x => (x.Resource, x.Units)));
                CollectionAssert.AreEqual(a.Receive.Select(x => (x.Resource, x.Units)), b.Receive.Select(x => (x.Resource, x.Units)));
            }
        }

        [Test]
        public void SaveLoad_KeepsTheRemainingTimeRatherThanRestartingIt()
        {
            _manager.InitializeNewGame();
            Wait(500);

            Assert.AreEqual(_config.OfferRefreshTimeSeconds - 500, Reloaded().SecondsUntilRefresh, 0.01f);
        }

        [Test]
        public void SaveLoad_RestoresWarehouseLevelsAndDerivedCapacity()
        {
            _manager.InitializeNewGame();
            _ledger.AddCrystals(1000);
            _manager.TryUpgradeWithCrystals(Metal);
            _manager.TryUpgradeWithCrystals(Metal);

            var restored = Reloaded();

            Assert.AreEqual(2, restored.WarehouseLevel(Metal));
            Assert.AreEqual(220, _ledger.GetTradeResource(Metal).Capacity);
        }

        [Test]
        public void LoadFromDto_DerivesCapacityFromTheLevel_NotFromTheLedgerDto()
        {
            var ledgerDto = new LedgerStateDto();
            ledgerDto.TradeResources[Stone.ToString()] = new TradeResourceStateDto { Capacity = 999, Amount = 0 };
            _ledger.LoadFromDto(ledgerDto);

            var state = StateWith();
            state.WarehouseLevels[Stone.ToString()] = 2;
            _manager.LoadFromDto(state);

            Assert.AreEqual(220, _ledger.GetTradeResource(Stone).Capacity, "A retuned curve must reach existing saves.");
        }

        [Test]
        public void LoadFromDto_ClampsALevelAboveTheCap()
        {
            var state = StateWith();
            state.WarehouseLevels[Stone.ToString()] = 9999;
            state.WarehouseLevels[Wood.ToString()]  = -3;

            _manager.LoadFromDto(state);

            Assert.AreEqual(19, _manager.WarehouseLevel(Stone), "Shown as level 20, the cap.");
            Assert.AreEqual(221_680, _ledger.GetTradeResource(Stone).Capacity);
            Assert.AreEqual(0, _manager.WarehouseLevel(Wood));
        }

        [Test]
        public void LoadFromDto_Null_StartsAFreshGame()
        {
            _manager.LoadFromDto(null);

            Assert.AreEqual(_config.OfferCount, _manager.Offers.Count);
        }

        [Test]
        public void LoadFromDto_WithNoOffers_GeneratesOneFreshBatch()
        {
            _manager.LoadFromDto(StateWith());

            Assert.AreEqual(_config.OfferCount, _manager.Offers.Count);
        }

        [Test]
        public void LoadFromDto_GeneratesFromRestoredCapacities()
        {
            var state = StateWith();
            foreach (var resource in AllResources) state.WarehouseLevels[resource.ToString()] = 10;

            _manager.LoadFromDto(state);

            Assert.That(_manager.Offers.Select(o => o.TotalGive), Is.All.GreaterThanOrEqualTo(2308),
                "Generated before the capacities were restored, these would be level-0 offers.");
        }

        [Test]
        public void LoadFromDto_GarbageTimestamp_FallsBackToAFullInterval()
        {
            var state = StateWith(StandardOffer());
            state.NextOfferRefreshDueUtc = "not-a-date";

            _manager.LoadFromDto(state);

            Assert.AreEqual(_config.OfferRefreshTimeSeconds, _manager.SecondsUntilRefresh, 0.01f);
        }

        [Test]
        public void LoadFromDto_UnknownResourceName_IsSkipped()
        {
            var offer = StandardOffer();
            offer.Give["Unobtainium"] = 50;

            LoadOffers(offer);

            Assert.AreEqual(2, _manager.Offers.Single().Give.Count);
        }

        [Test]
        public void LoadFromDto_NumericNames_AreNotMistakenForResources()
        {
            var offer = StandardOffer();
            offer.Give["4"] = 50;   // Enum.TryParse alone would read this as Leather

            LoadOffers(offer);

            Assert.IsFalse(_manager.Offers.Single().Give.Any(a => a.Resource == TradeResourceType.Leather));
        }

        [Test]
        public void LoadFromDto_DropsAnOfferWithAnEmptySide()
        {
            var damaged = StandardOffer("bad");
            damaged.Receive = Units((Clay, 0));

            LoadOffers(damaged, StandardOffer("good"));

            Assert.AreEqual("good", _manager.Offers.Single().Id);
        }

        [Test]
        public void LoadFromDto_ReadsASchema7OfferWithDecimalAmounts()
        {
            var json = @"{
                ""nextOfferRefreshDueUtc"": """ + _clock.UtcNow.AddSeconds(300).ToString("O") + @""",
                ""warehouseLevels"": { ""Stone"": 2 },
                ""activeOffers"": [ { ""id"": ""old"", ""profitability"": ""Profitable"",
                                     ""give"":    { ""Stone"": 17.0, ""Wood"": 23.0 },
                                     ""receive"": { ""Clay"": 44.0, ""Metal"": 12.0 } } ] }";

            _manager.LoadFromDto(Newtonsoft.Json.JsonConvert.DeserializeObject<TradeStateDto>(json));

            var offer = _manager.Offers.Single();
            Assert.AreEqual("old", offer.Id);
            Assert.AreEqual(40, offer.TotalGive);
            Assert.AreEqual(56, offer.TotalReceive);
            Assert.AreEqual(2, _manager.WarehouseLevel(Stone));
        }

        [Test]
        public void SaveLoad_SurvivesARealJsonRoundTrip()
        {
            _manager.InitializeNewGame();

            var json     = Newtonsoft.Json.JsonConvert.SerializeObject(_manager.ToDto());
            var restored = new TradeManager(_ledger, _clock, _config, new System.Random(7));
            restored.LoadFromDto(Newtonsoft.Json.JsonConvert.DeserializeObject<TradeStateDto>(json));

            CollectionAssert.AreEqual(_manager.Offers.Select(o => o.Id), restored.Offers.Select(o => o.Id));
            CollectionAssert.AreEqual(_manager.Offers.Select(o => o.TotalReceive), restored.Offers.Select(o => o.TotalReceive));
            // A key's colon follows its closing quote; the colons inside a timestamp do not.
            Assert.IsFalse(Regex.IsMatch(json, @""":\s*-?\d+\.\d"), "Amounts are written as whole numbers.");
            StringAssert.Contains(@"""give"":{", json);
        }
    }
}
