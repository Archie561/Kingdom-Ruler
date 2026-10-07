using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using KingdomRuler.Systems.Events;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Systems.Clock;
using KingdomRuler.Systems.Save;
using KingdomRuler.Modules.Trade;
using KingdomRuler.Modules.Trade.Domain;

namespace KingdomRuler.Tests.EditMode.Modules.Trade
{
    /// <summary>
    /// Shared test clock for this module's namespace. Also used by the generator and presenter
    /// tests — do not redeclare it in another file in this namespace.
    /// </summary>
    public sealed class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; } = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        public void Advance(TimeSpan by) => UtcNow += by;
    }

    [TestFixture]
    public sealed class TradeResourcePairTests
    {
        [TestCase(TradeResourceType.Stone,    TradeResourceType.Wood)]
        [TestCase(TradeResourceType.Wood,     TradeResourceType.Stone)]
        [TestCase(TradeResourceType.Metal,    TradeResourceType.Minerals)]
        [TestCase(TradeResourceType.Minerals, TradeResourceType.Metal)]
        [TestCase(TradeResourceType.Leather,  TradeResourceType.Clay)]
        [TestCase(TradeResourceType.Clay,     TradeResourceType.Leather)]
        public void GetPairedResource_MatchesTheGddPairing(TradeResourceType from, TradeResourceType to)
        {
            Assert.AreEqual(to, TradeResourcePair.GetPairedResource(from));
        }

        [Test]
        public void Pairing_IsSymmetric()
        {
            foreach (TradeResourceType type in Enum.GetValues(typeof(TradeResourceType)))
            {
                var paired = TradeResourcePair.GetPairedResource(type);
                Assert.AreEqual(type, TradeResourcePair.GetPairedResource(paired));
                Assert.AreNotEqual(type, paired, "A resource cannot pair with itself.");
            }
        }
    }

    [TestFixture]
    public sealed class WarehouseUpgradeCalculatorTests
    {
        [Test]
        public void PairedPathCost_Is80PercentOfCapacity()
        {
            Assert.AreEqual(80f, WarehouseUpgradeCalculator.PairedPathCost(100f), 0.001f);
            Assert.AreEqual(0f,  WarehouseUpgradeCalculator.PairedPathCost(-50f), 0.001f);
        }

        [Test]
        public void CrystalPathCost_IsAlwaysAtLeastOne()
        {
            var tiny = new WarehouseCurve(0.001f, 1.01f, 1f);

            for (int level = 0; level <= 20; level++)
                Assert.GreaterOrEqual(WarehouseUpgradeCalculator.CrystalPathCost(level, tiny), 1);
        }

        [Test]
        public void CrystalPathCost_IsStrictlyIncreasing()
        {
            // The property the old array's `5 + 3*level` fallback broke: past the table, cost grew
            // linearly while capacity grew exponentially, so a level-50 warehouse was cheap.
            var curve = WarehouseCurve.DefaultCrystalCost;
            int previous = WarehouseUpgradeCalculator.CrystalPathCost(0, curve);

            for (int level = 1; level <= 40; level++)
            {
                int current = WarehouseUpgradeCalculator.CrystalPathCost(level, curve);
                Assert.Greater(current, previous, $"Cost did not rise at level {level}.");
                previous = current;
            }
        }

        [Test]
        public void CrystalPathCost_StaysFiniteAtAnAbsurdLevel()
        {
            Assert.Greater(
                WarehouseUpgradeCalculator.CrystalPathCost(200, WarehouseCurve.DefaultCrystalCost), 0);
        }

        [Test]
        public void CapacityAtLevel_ClampsANegativeLevel()
        {
            var curve = WarehouseCurve.DefaultCapacity;

            Assert.AreEqual(curve.ValueAt(0),
                            WarehouseUpgradeCalculator.CapacityAtLevel(-5, curve), 0.001f);
        }
    }

    [TestFixture]
    public sealed class TradeManagerTests
    {
        private FakeClock     _clock;
        private EventBus      _bus;
        private KingdomLedger _ledger;
        private TradeConfig   _config;
        private TradeManager  _manager;

        [SetUp]
        public void SetUp()
        {
            _clock  = new FakeClock();
            _bus    = new EventBus();
            _ledger = new KingdomLedger(_bus);
            _config = ScriptableObject.CreateInstance<TradeConfig>();
            _manager = new TradeManager(_ledger, _clock, _config, new System.Random(4242));
        }

        [TearDown]
        public void TearDown()
        {
            if (_config != null) UnityEngine.Object.DestroyImmediate(_config);
        }

        /// <summary>Fill every warehouse so the give side of any offer is affordable.</summary>
        private void StockUp(float capacity = 20000f)
        {
            foreach (TradeResourceType type in Enum.GetValues(typeof(TradeResourceType)))
            {
                _ledger.SetWarehouseCapacity(type, capacity);
                _ledger.AddTradeResource(type, capacity);
            }
        }

        // ── Construction and initialisation ───────────────────────────────────────

        [Test]
        public void Constructor_GeneratesNoOffers()
        {
            // The old ctor generated a batch before the save file had even been read, which is
            // why relaunching the app rerolled the list for free.
            Assert.IsEmpty(_manager.ActiveOffers);
            Assert.IsNull(_manager.NextRefreshDueUtc);
        }

        [Test]
        public void InitializeNewGame_FillsTheListAndStartsTheTimer()
        {
            _manager.InitializeNewGame();

            Assert.AreEqual(_config.OfferCount, _manager.ActiveOffers.Count);
            Assert.AreEqual(_config.OfferRefreshTimeSeconds, _manager.GetSecondsUntilRefresh(), 1f);
        }

        [Test]
        public void InitializeNewGame_SetsBaseCapacityAndTheDerivedRegenRate()
        {
            _manager.InitializeNewGame();

            var state = _ledger.GetTradeResource(TradeResourceType.Stone);
            Assert.AreEqual(_config.ToCapacityCurve().ValueAt(0), state.Capacity, 0.001f);
            Assert.AreEqual(state.Capacity / 86400f, state.RegenRatePerSecond, 0.0001f);
        }

        // ── Refresh ───────────────────────────────────────────────────────────────

        [Test]
        public void ProcessTick_BeforeTheDeadline_KeepsTheSameOffers()
        {
            _manager.InitializeNewGame();
            var before = _manager.ActiveOffers.Select(o => o.Id).ToList();

            _clock.Advance(TimeSpan.FromMinutes(19));
            _manager.ProcessTick();

            CollectionAssert.AreEqual(before, _manager.ActiveOffers.Select(o => o.Id).ToList());
        }

        [Test]
        public void ProcessTick_AfterTheDeadline_ReplacesTheOffers()
        {
            _manager.InitializeNewGame();
            var before = _manager.ActiveOffers.Select(o => o.Id).ToList();

            _clock.Advance(TimeSpan.FromMinutes(21));
            _manager.ProcessTick();

            CollectionAssert.AreNotEqual(before, _manager.ActiveOffers.Select(o => o.Id).ToList());
            Assert.AreEqual(_config.OfferCount, _manager.ActiveOffers.Count);
        }

        [Test]
        public void ProcessTick_RaisesTradeStateChangedOnlyWhenSomethingChanged()
        {
            _manager.InitializeNewGame();
            int raised = 0;
            _manager.TradeStateChanged += () => raised++;

            _clock.Advance(TimeSpan.FromMinutes(5));
            _manager.ProcessTick();
            Assert.AreEqual(0, raised, "A quiet tick must not force a UI rebuild.");

            _clock.Advance(TimeSpan.FromMinutes(20));
            _manager.ProcessTick();
            Assert.AreEqual(1, raised);
        }

        [Test]
        public void ProcessTick_RegeneratesResourcesOverTime()
        {
            // The regression that matters most: before this module had a tick driver, regen only
            // ever ran at cold boot, so a player watching the screen gained nothing.
            _manager.InitializeNewGame();
            float before = _ledger.GetTradeResource(TradeResourceType.Stone).Amount;

            _clock.Advance(TimeSpan.FromHours(6));
            _manager.ProcessTick();

            Assert.Greater(_ledger.GetTradeResource(TradeResourceType.Stone).Amount, before);
        }

        [Test]
        public void Regen_FillsAnEmptyWarehouseInTwentyFourHours()
        {
            _manager.InitializeNewGame();
            var state = _ledger.GetTradeResource(TradeResourceType.Clay);

            _clock.Advance(TimeSpan.FromHours(24));
            _manager.ProcessTick();

            Assert.AreEqual(state.Capacity, state.Amount, state.Capacity * 0.01f);
        }

        [Test]
        public void ABackwardsDeviceClock_DoesNotStallRegenForever()
        {
            // AccruePassiveRegen used to return early when its baseline was in the future, and
            // nothing else ever moves that baseline back — so a device clock that ran ahead and
            // was later corrected stopped the warehouse regenerating permanently. It resyncs now.
            _manager.InitializeNewGame();

            _clock.Advance(TimeSpan.FromDays(-30));      // clock corrected backwards
            _manager.ProcessTick();                       // resyncs, accrues nothing

            float afterResync = _ledger.GetTradeResource(TradeResourceType.Stone).Amount;
            _clock.Advance(TimeSpan.FromHours(6));
            _manager.ProcessTick();

            Assert.Greater(_ledger.GetTradeResource(TradeResourceType.Stone).Amount, afterResync,
                "Regen must resume once time moves forward again.");
        }

        [Test]
        public void InstantRefresh_SpendsCrystalsAndResetsTheTimer()
        {
            _manager.InitializeNewGame();
            _ledger.AddCrystals(10);
            _clock.Advance(TimeSpan.FromMinutes(15));
            var before = _manager.ActiveOffers.Select(o => o.Id).ToList();

            Assert.IsTrue(_manager.InstantRefresh());

            Assert.AreEqual(10 - _config.InstantRefreshCrystalCost, _ledger.Crystals);
            CollectionAssert.AreNotEqual(before, _manager.ActiveOffers.Select(o => o.Id).ToList());
            Assert.AreEqual(_config.OfferRefreshTimeSeconds, _manager.GetSecondsUntilRefresh(), 1f);
        }

        [Test]
        public void InstantRefresh_WithoutEnoughCrystals_ChangesNothing()
        {
            _manager.InitializeNewGame();
            _ledger.AddCrystals(_config.InstantRefreshCrystalCost - 1);
            var before = _manager.ActiveOffers.Select(o => o.Id).ToList();

            Assert.IsFalse(_manager.InstantRefresh());

            CollectionAssert.AreEqual(before, _manager.ActiveOffers.Select(o => o.Id).ToList());
        }

        // ── Evaluate ──────────────────────────────────────────────────────────────

        [Test]
        public void Evaluate_UnknownId_ReportsUnavailable()
        {
            _manager.InitializeNewGame();

            var evaluation = _manager.Evaluate("no-such-offer");

            Assert.IsFalse(evaluation.OfferExists);
            Assert.IsFalse(evaluation.CanAccept);
            Assert.AreEqual(TradeIssueKind.OfferUnavailable, evaluation.Issues[0].Kind);
        }

        [Test]
        public void Evaluate_WhenShortOnAGiveResource_NamesThatResource()
        {
            _manager.InitializeNewGame();
            var offer = _manager.ActiveOffers[0];
            var short_ = offer.GiveResources.First();

            var evaluation = _manager.Evaluate(offer.Id);

            Assert.IsFalse(evaluation.CanAccept, "An empty warehouse cannot pay for anything.");
            var issue = evaluation.Issues.First(i => i.Kind == TradeIssueKind.InsufficientResource
                                                  && i.Resource == short_.Key);
            Assert.AreEqual(short_.Value, issue.Required, 0.001f);
            Assert.IsTrue(issue.BlocksAcceptance);
        }

        [Test]
        public void Evaluate_MutatesNothing()
        {
            _manager.InitializeNewGame();
            StockUp();
            var offer = _manager.ActiveOffers[0];
            var before = _ledger.TakeSnapshot();

            for (int i = 0; i < 50; i++) _manager.Evaluate(offer.Id);

            var after = _ledger.TakeSnapshot();
            foreach (TradeResourceType type in Enum.GetValues(typeof(TradeResourceType)))
                Assert.AreEqual(before.TradeResourceAmounts[type], after.TradeResourceAmounts[type], 0.001f);
            Assert.AreEqual(_config.OfferCount, _manager.ActiveOffers.Count);
        }

        // ── Overflow warns, it does not block ─────────────────────────────────────

        [Test]
        public void Evaluate_WhenAWarehouseIsFull_WarnsButStaysAcceptable()
        {
            // The design rule: a full warehouse is shown to the player, who may accept anyway and
            // forfeit the excess. Only being unable to pay refuses a trade.
            _manager.InitializeNewGame();
            StockUp();
            var offer = _manager.ActiveOffers[0];

            var evaluation = _manager.Evaluate(offer.Id);

            Assert.IsTrue(evaluation.HasOverflowWarning, "Every warehouse is full, so this must warn.");
            Assert.IsTrue(evaluation.CanAccept, "Overflow must NOT block acceptance.");
            Assert.IsTrue(evaluation.Issues.Any(i => i.Kind == TradeIssueKind.WouldOverflowWarehouse));
            Assert.IsFalse(evaluation.Issues
                .Where(i => i.Kind == TradeIssueKind.WouldOverflowWarehouse)
                .Any(i => i.BlocksAcceptance));
        }

        [Test]
        public void AcceptOffer_WithAFullWarehouse_SucceedsAndReportsWhatWasForfeited()
        {
            _manager.InitializeNewGame();
            StockUp();
            var offer = _manager.ActiveOffers[0];

            var result = _manager.AcceptOffer(offer.Id);

            Assert.IsTrue(result.Accepted, "A full warehouse must not refuse the trade.");
            Assert.IsTrue(result.HasForfeited, "The excess should be reported, not silently lost.");
            Assert.Greater(result.TotalForfeited, 0f);

            // The invariant is received == min(requested, freeSpace), NOT received == requested.
            foreach (var pair in offer.ReceiveResources)
                Assert.LessOrEqual(result.Received[pair.Key], pair.Value);
        }

        [Test]
        public void AcceptOffer_WithRoomToSpare_ForfeitsNothing()
        {
            _manager.InitializeNewGame();
            StockUp();
            var offer = _manager.ActiveOffers[0];

            // Make room on the receive side only.
            foreach (var pair in offer.ReceiveResources)
                _ledger.SpendTradeResource(pair.Key, pair.Value * 2f);

            var result = _manager.AcceptOffer(offer.Id);

            Assert.IsTrue(result.Accepted);
            Assert.IsFalse(result.HasForfeited);
            foreach (var pair in offer.ReceiveResources)
                Assert.AreEqual(pair.Value, result.Received[pair.Key], 0.001f);
        }

        // ── Accept ────────────────────────────────────────────────────────────────

        [Test]
        public void AcceptOffer_MovesResourcesAndRemovesTheOffer()
        {
            _manager.InitializeNewGame();
            StockUp();
            var offer = _manager.ActiveOffers[0];
            var give  = offer.GiveResources.First();
            float heldBefore = _ledger.GetTradeResource(give.Key).Amount;

            var result = _manager.AcceptOffer(offer.Id);

            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(heldBefore - give.Value, _ledger.GetTradeResource(give.Key).Amount, 0.001f);
            Assert.IsFalse(_manager.ActiveOffers.Any(o => o.Id == offer.Id));
        }

        [Test]
        public void AcceptOffer_ConsumesTheOfferWithoutReplacingIt()
        {
            _manager.InitializeNewGame();
            StockUp();

            _manager.AcceptOffer(_manager.ActiveOffers[0].Id);

            Assert.AreEqual(_config.OfferCount - 1, _manager.ActiveOffers.Count);
        }

        [Test]
        public void AcceptOffer_WhenShortOnAGiveResource_IsRefusedAndSpendsNothing()
        {
            _manager.InitializeNewGame();
            var offer = _manager.ActiveOffers[0];
            var before = _ledger.TakeSnapshot();

            var result = _manager.AcceptOffer(offer.Id);

            Assert.IsFalse(result.Accepted);
            Assert.IsTrue(result.Issues.Any(i => i.Kind == TradeIssueKind.InsufficientResource));
            Assert.IsEmpty(result.Received);
            foreach (TradeResourceType type in Enum.GetValues(typeof(TradeResourceType)))
                Assert.AreEqual(before.TradeResourceAmounts[type],
                                _ledger.GetTradeResource(type).Amount, 0.001f);
            Assert.AreEqual(_config.OfferCount, _manager.ActiveOffers.Count, "A refusal keeps the offer.");
        }

        [Test]
        public void AcceptOffer_WithAStaleId_IsRefusedAndTouchesNothing()
        {
            // The 20-minute refresh can land between tapping a row and confirming. An index-based
            // accept would execute whichever offer had taken that slot; an id turns it into this.
            _manager.InitializeNewGame();
            StockUp();
            var stale = _manager.ActiveOffers[0].Id;

            _clock.Advance(TimeSpan.FromMinutes(21));
            _manager.ProcessTick();

            var result = _manager.AcceptOffer(stale);

            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(TradeIssueKind.OfferUnavailable, result.Issues[0].Kind);
        }

        [Test]
        public void AcceptOffer_RaisesTradeStateChangedExactlyOnce()
        {
            _manager.InitializeNewGame();
            StockUp();
            int raised = 0;
            _manager.TradeStateChanged += () => raised++;

            _manager.AcceptOffer(_manager.ActiveOffers[0].Id);

            Assert.AreEqual(1, raised);
        }

        [Test]
        public void AcceptOffer_AccruesRegenFirst_SoAJustCrossedThresholdIsHonoured()
        {
            // Correctness must not depend on whether a tick happened to land first.
            _manager.InitializeNewGame();
            var offer = _manager.ActiveOffers[0];

            // Let regen alone make the offer affordable, without calling ProcessTick.
            _clock.Advance(TimeSpan.FromHours(48));

            var result = _manager.AcceptOffer(offer.Id);

            Assert.IsTrue(result.Accepted,
                "Regen accrued inside AcceptOffer should have made the give side affordable.");
        }

        // ── Warehouse upgrades ────────────────────────────────────────────────────

        [Test]
        public void UpgradeWithCrystals_RaisesTheLevelAndTheCapacity()
        {
            _manager.InitializeNewGame();
            _ledger.AddCrystals(1000);
            float before = _ledger.GetTradeResource(TradeResourceType.Stone).Capacity;

            Assert.IsTrue(_manager.UpgradeWarehouseWithCrystals(TradeResourceType.Stone));

            Assert.AreEqual(1, _manager.GetWarehouseLevel(TradeResourceType.Stone));
            Assert.Greater(_ledger.GetTradeResource(TradeResourceType.Stone).Capacity, before);
        }

        [Test]
        public void UpgradeWithCrystals_ChargesExactlyThePriceItQuotes()
        {
            // Price shown == price charged. A UI that computes the price separately from the code
            // that deducts it is a monetization bug (docs/modules/Laws.md §6.3).
            _manager.InitializeNewGame();
            _ledger.AddCrystals(1000);
            int quoted = _manager.GetWarehouseCrystalCost(TradeResourceType.Wood);

            _manager.UpgradeWarehouseWithCrystals(TradeResourceType.Wood);

            Assert.AreEqual(1000 - quoted, _ledger.Crystals);
        }

        [Test]
        public void UpgradeWithCrystals_WithoutEnough_ChangesNothing()
        {
            _manager.InitializeNewGame();

            Assert.IsFalse(_manager.UpgradeWarehouseWithCrystals(TradeResourceType.Metal));
            Assert.AreEqual(0, _manager.GetWarehouseLevel(TradeResourceType.Metal));
        }

        [Test]
        public void UpgradePaired_Costs80PercentOfPairedCapacity_TakenFromPairedStock()
        {
            // GDD §7's confirmed reading: the price is computed from the paired warehouse's
            // CAPACITY and deducted from its STORED amount.
            _manager.InitializeNewGame();
            var paired = _manager.GetPairedResource(TradeResourceType.Stone);
            float pairedCapacity = _ledger.GetTradeResource(paired).Capacity;
            _ledger.AddTradeResource(paired, pairedCapacity);       // fill it

            float expected = pairedCapacity * 0.8f;
            float quoted   = _manager.GetWarehousePairedCost(TradeResourceType.Stone);
            Assert.AreEqual(expected, quoted, 0.001f);

            Assert.IsTrue(_manager.UpgradeWarehouseWithPairedResource(TradeResourceType.Stone));

            Assert.AreEqual(pairedCapacity - expected,
                            _ledger.GetTradeResource(paired).Amount, 0.01f);
            Assert.AreEqual(1, _manager.GetWarehouseLevel(TradeResourceType.Stone));
        }

        [Test]
        public void UpgradePaired_DoesNotChangeThePairedWarehousesOwnCapacity()
        {
            _manager.InitializeNewGame();
            var paired = _manager.GetPairedResource(TradeResourceType.Leather);
            float pairedCapacity = _ledger.GetTradeResource(paired).Capacity;
            _ledger.AddTradeResource(paired, pairedCapacity);

            _manager.UpgradeWarehouseWithPairedResource(TradeResourceType.Leather);

            Assert.AreEqual(pairedCapacity, _ledger.GetTradeResource(paired).Capacity, 0.001f);
            Assert.AreEqual(0, _manager.GetWarehouseLevel(paired));
        }

        [Test]
        public void UpgradePaired_WithoutEnoughStock_ChangesNothing()
        {
            _manager.InitializeNewGame();   // warehouses start empty

            Assert.IsFalse(_manager.UpgradeWarehouseWithPairedResource(TradeResourceType.Clay));
            Assert.AreEqual(0, _manager.GetWarehouseLevel(TradeResourceType.Clay));
        }

        [Test]
        public void AfterAnyUpgrade_RegenRateStaysCapacityOver24Hours()
        {
            // GDD §7's headline rule: a bigger warehouse produces more per hour but still fills
            // in the same 24 hours.
            _manager.InitializeNewGame();
            _ledger.AddCrystals(100000);

            for (int i = 0; i < 5; i++)
            {
                _manager.UpgradeWarehouseWithCrystals(TradeResourceType.Minerals);
                var state = _ledger.GetTradeResource(TradeResourceType.Minerals);
                Assert.AreEqual(state.Capacity / 86400f, state.RegenRatePerSecond, 0.0001f,
                    $"Fill time drifted after upgrade {i + 1}.");
            }
        }

        [Test]
        public void UpgradesStopAtTheSupportedCeiling()
        {
            // A save-integrity guard rather than a design cap — see WarehouseCurve. Reached via
            // a save rather than by buying 50 upgrades: the price curve is exponential, so the
            // crystals needed exceed int.MaxValue long before the ceiling does.
            var dto = new TradeStateDto();
            dto.WarehouseLevels[TradeResourceType.Wood.ToString()] = WarehouseCurve.MaxSupportedLevel;
            _manager.LoadFromDto(dto);
            _ledger.AddCrystals(int.MaxValue);

            Assert.IsFalse(_manager.CanUpgradeFurther(TradeResourceType.Wood));
            Assert.IsFalse(_manager.UpgradeWarehouseWithCrystals(TradeResourceType.Wood));
            Assert.AreEqual(WarehouseCurve.MaxSupportedLevel,
                            _manager.GetWarehouseLevel(TradeResourceType.Wood));
            Assert.AreEqual(int.MaxValue, _ledger.Crystals, "A refused upgrade must not charge.");
        }

        // ── Save / load ───────────────────────────────────────────────────────────

        [Test]
        public void RelaunchWithoutTimePassing_DoesNotRerollTheOffers()
        {
            // The exploit this closes: offers used to be regenerated in the constructor, so
            // quitting and reopening was a free instant refresh.
            _manager.InitializeNewGame();
            var expected = _manager.ActiveOffers.Select(o => o.Id).ToList();

            var reloaded = new TradeManager(_ledger, _clock, _config, new System.Random(1));
            reloaded.LoadFromDto(_manager.ToDto());

            CollectionAssert.AreEqual(expected, reloaded.ActiveOffers.Select(o => o.Id).ToList());
        }

        [Test]
        public void SaveLoad_RestoresOfferTermsExactly()
        {
            _manager.InitializeNewGame();
            var original = _manager.ActiveOffers[0];

            var reloaded = new TradeManager(_ledger, _clock, _config, new System.Random(1));
            reloaded.LoadFromDto(_manager.ToDto());
            var restored = reloaded.ActiveOffers.First(o => o.Id == original.Id);

            Assert.AreEqual(original.Profitability, restored.Profitability);
            CollectionAssert.AreEquivalent(original.GiveResources, restored.GiveResources);
            CollectionAssert.AreEquivalent(original.ReceiveResources, restored.ReceiveResources);
        }

        [Test]
        public void SaveLoad_KeepsTheRemainingTimeRatherThanRestartingIt()
        {
            _manager.InitializeNewGame();
            _clock.Advance(TimeSpan.FromMinutes(10));

            var reloaded = new TradeManager(_ledger, _clock, _config, new System.Random(1));
            reloaded.LoadFromDto(_manager.ToDto());

            Assert.AreEqual(600f, reloaded.GetSecondsUntilRefresh(), 5f);
        }

        [Test]
        public void SaveLoad_RestoresWarehouseLevelsAndDerivedCapacity()
        {
            _manager.InitializeNewGame();
            _ledger.AddCrystals(10000);
            _manager.UpgradeWarehouseWithCrystals(TradeResourceType.Metal);
            _manager.UpgradeWarehouseWithCrystals(TradeResourceType.Metal);

            var reloaded = new TradeManager(_ledger, _clock, _config, new System.Random(1));
            reloaded.LoadFromDto(_manager.ToDto());

            Assert.AreEqual(2, reloaded.GetWarehouseLevel(TradeResourceType.Metal));
            Assert.AreEqual(_config.ToCapacityCurve().ValueAt(2),
                            _ledger.GetTradeResource(TradeResourceType.Metal).Capacity, 0.001f);
        }

        [Test]
        public void LoadFromDto_DerivesCapacityFromTheLevel_NotFromTheLedgerDto()
        {
            // warehouseLevels is authoritative, so a retuned curve reaches existing saves instead
            // of being frozen at whatever capacity was written when the player last upgraded.
            var dto = new TradeStateDto();
            dto.WarehouseLevels[TradeResourceType.Stone.ToString()] = 3;

            _config.CapacityBase = 200f;      // retune after the save was written
            _manager.LoadFromDto(dto);

            Assert.AreEqual(_config.ToCapacityCurve().ValueAt(3),
                            _ledger.GetTradeResource(TradeResourceType.Stone).Capacity, 0.001f);
        }

        [Test]
        public void LoadFromDto_ClampsAnAbsurdWarehouseLevel()
        {
            // The save is plain JSON on the device. Unclamped, 100 × 1.5^9999 is Infinity, which
            // makes the regen rate infinite and every amount NaN — an unrecoverable economy.
            var dto = new TradeStateDto();
            dto.WarehouseLevels[TradeResourceType.Stone.ToString()] = 9999;

            _manager.LoadFromDto(dto);

            Assert.AreEqual(WarehouseCurve.MaxSupportedLevel,
                            _manager.GetWarehouseLevel(TradeResourceType.Stone));
            var capacity = _ledger.GetTradeResource(TradeResourceType.Stone).Capacity;
            Assert.IsFalse(float.IsInfinity(capacity));
            Assert.IsFalse(float.IsNaN(capacity));
        }

        [Test]
        public void LoadFromDto_Null_StartsAFreshGame()
        {
            _manager.LoadFromDto(null);

            Assert.AreEqual(_config.OfferCount, _manager.ActiveOffers.Count);
        }

        [Test]
        public void LoadFromDto_V4SaveWithNoOffers_GeneratesOneFreshBatch()
        {
            // A pre-v5 save deserializes activeOffers as null. The player gets a batch rather
            // than an empty screen; that is the documented one-time cost of the schema change.
            var dto = new TradeStateDto { ActiveOffers = null };

            _manager.LoadFromDto(dto);

            Assert.AreEqual(_config.OfferCount, _manager.ActiveOffers.Count);
        }

        [Test]
        public void LoadFromDto_GarbageTimestamp_FallsBackToAFullInterval()
        {
            var dto = new TradeStateDto { NextOfferRefreshDueUtc = "not-a-date" };

            Assert.DoesNotThrow(() => _manager.LoadFromDto(dto));
            Assert.AreEqual(_config.OfferRefreshTimeSeconds, _manager.GetSecondsUntilRefresh(), 2f);
        }

        [Test]
        public void LoadFromDto_UnknownResourceName_IsSkipped()
        {
            var dto = new TradeStateDto();
            dto.WarehouseLevels["Unobtanium"] = 4;
            dto.WarehouseLevels[TradeResourceType.Clay.ToString()] = 2;

            Assert.DoesNotThrow(() => _manager.LoadFromDto(dto));
            Assert.AreEqual(2, _manager.GetWarehouseLevel(TradeResourceType.Clay));
        }

        [Test]
        public void LoadFromDto_DropsAnOfferWithAnEmptySide()
        {
            // A one-sided offer would be free resources or a no-op trade.
            var dto = new TradeStateDto();
            dto.ActiveOffers.Add(new TradeOfferDto
            {
                Id = "broken",
                Profitability = "Neutral",
                Give = new Dictionary<string, float> { { "Stone", 5f } },
                Receive = new Dictionary<string, float>()
            });

            _manager.LoadFromDto(dto);

            Assert.IsFalse(_manager.ActiveOffers.Any(o => o.Id == "broken"));
        }

        [Test]
        public void SaveLoad_SurvivesARealJsonRoundTrip()
        {
            // Catches a Newtonsoft mapping mistake on the nested TradeOfferDto — the class of bug
            // that only shows up once the save actually reaches disk.
            _manager.InitializeNewGame();
            _ledger.AddCrystals(10000);
            _manager.UpgradeWarehouseWithCrystals(TradeResourceType.Wood);

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(_manager.ToDto());
            var round  = Newtonsoft.Json.JsonConvert.DeserializeObject<TradeStateDto>(json);

            var reloaded = new TradeManager(_ledger, _clock, _config, new System.Random(1));
            reloaded.LoadFromDto(round);

            Assert.AreEqual(_manager.ActiveOffers.Count, reloaded.ActiveOffers.Count);
            Assert.AreEqual(1, reloaded.GetWarehouseLevel(TradeResourceType.Wood));
            CollectionAssert.AreEqual(
                _manager.ActiveOffers.Select(o => o.Id).ToList(),
                reloaded.ActiveOffers.Select(o => o.Id).ToList());
        }
    }
}
