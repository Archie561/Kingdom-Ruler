using System;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine;
using KingdomRuler.Systems.Events;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Systems.Popups;
using KingdomRuler.Modules.Laws;
using KingdomRuler.Modules.Laws.Domain;
using KingdomRuler.Modules.Laws.Presenters;
using KingdomRuler.Tests.EditMode.Systems;
using KingdomRuler.Tests.EditMode.Systems.Ledger;

namespace KingdomRuler.Tests.EditMode.Modules.Laws
{
    // Test fakes live in Tests/EditMode/Systems/TestServiceFakes.cs — promoted there once
    // Trade needed the identical ones. FakeClock is still declared in LawsDomainTests.cs
    // in this namespace.


    // ── Tests ─────────────────────────────────────────────────────────────────────

    [TestFixture]
    public sealed class LawsPresenterTests
    {
        private FakeClock         _clock;
        private EventBus          _eventBus;
        private KingdomLedger     _ledger;
        private LawsConfig        _config;
        private LawsManager       _manager;
        private FakeAudioService  _audio;
        private FakeHapticService _haptics;
        private FakeLocalizationService _localization;
        private CharacteristicRegistry  _characteristics;
        private PopupSystem            _popups;
        private LawsPresenter     _presenter;

        private readonly List<ScriptableObject> _createdAssets = new();
        private readonly List<UnityEngine.Object> _createdObjects = new();

        [SetUp]
        public void SetUp()
        {
            _clock    = new FakeClock();
            _eventBus = new EventBus();
            _ledger   = new KingdomLedger(_eventBus);

            _config = ScriptableObject.CreateInstance<LawsConfig>();
            _config.MaxHeldCards             = 8;
            _config.CardReplenishTimeSeconds = 120f;
            _config.CrystalCostPerRefill     = 2;
            _config.CrystalBuyUpDivisor      = 20f;
            _createdAssets.Add(_config);

            _manager   = new LawsManager(_ledger, _clock, _config);
            _audio     = new FakeAudioService();
            _haptics   = new FakeHapticService();
            _localization = new FakeLocalizationService();
            _characteristics = TestCharacteristics.Complete(_createdObjects);

            // A bare manager with no registry: opening any popup throws, so nothing is confirmed.
            // Flows that actually open one are verified in Play mode (ARCHITECTURE.md §8).
            var popupHost = new GameObject("popups");
            _createdObjects.Add(popupHost);
            _popups = popupHost.AddComponent<PopupSystem>();
            _presenter = new LawsPresenter(
                _manager, _ledger, _eventBus, _audio, _haptics, _localization, _characteristics, _popups);
        }

        [TearDown]
        public void TearDown()
        {
            _presenter.Dispose();
            foreach (var asset in _createdAssets)
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            _createdAssets.Clear();

            foreach (var obj in _createdObjects)
                if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            _createdObjects.Clear();
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private LawCardDefinition CreateCard(string id,
            LawCardEffect[] accept = null, LawCardEffect[] reject = null)
        {
            var card = ScriptableObject.CreateInstance<LawCardDefinition>();
            card.CardId        = id;
            card.AcceptEffects = accept;
            card.RejectEffects = reject;
            _createdAssets.Add(card);
            return card;
        }

        /// Advances the clock one replenishment cycle so one card becomes active.
        private LawCardDefinition PushOneCardToHeld(string id = "card_a",
            LawCardEffect[] accept = null,
            LawCardEffect[] reject = null)
        {
            var card = CreateCard(id, accept, reject);
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();
            _clock.Advance(TimeSpan.FromSeconds(120));
            _manager.ProcessReplenishment();
            return card;
        }

        // ── ActiveCard / held-card readout ────────────────────────────────────────

        [Test]
        public void ActiveCardDisplay_WhenNoCardHeld_IsUnavailable()
        {
            Assert.IsFalse(_presenter.HasActiveCard);
            Assert.IsFalse(_presenter.TryGetActiveCardDisplay(out _));
        }

        [Test]
        public void ActiveCardDisplay_WithCardInQueue_DescribesThatCard()
        {
            var card = PushOneCardToHeld();

            Assert.IsTrue(_presenter.TryGetActiveCardDisplay(out var display));
            Assert.AreEqual(card.CardId, display.CardId);
        }

        // ── Localization ──────────────────────────────────────────────────────────

        /// <summary>
        /// Card text is keyed off CardId, so there is nothing on the asset to wire and
        /// nothing to mistype. This pins the derivation.
        /// </summary>
        [Test]
        public void ActiveCardDisplay_ResolvesTitleAndFlavorFromTheCardId()
        {
            var card = PushOneCardToHeld("law_conscription");

            Assert.IsTrue(_presenter.TryGetActiveCardDisplay(out var display));
            Assert.AreEqual("LawCardsTable:law_conscription.title",  display.Title);
            Assert.AreEqual("LawCardsTable:law_conscription.flavor", display.FlavorText);
        }

        /// <summary>
        /// The Presenter resolves through <c>card.TitleKey</c> and the content tests check
        /// the table through the same properties. If they ever diverged, the content tests would
        /// confirm entries the game never asks for — a passing test and blank text on
        /// screen. This pins them together.
        /// </summary>
        [Test]
        public void CardKeys_ComeFromTheDefinitionsOwnDerivation()
        {
            var card = PushOneCardToHeld("law_conscription");

            Assert.AreEqual(LawCardDefinition.BuildTitleKey("law_conscription"),  card.TitleKey);
            Assert.AreEqual(LawCardDefinition.BuildFlavorKey("law_conscription"), card.FlavorKey);

            Assert.IsTrue(_presenter.TryGetActiveCardDisplay(out var display));
            Assert.AreEqual(
                LawCardDefinition.StringTable + ":" + card.TitleKey,  display.Title,
                "The Presenter must resolve the table and key the definition declares.");
            Assert.AreEqual(
                LawCardDefinition.StringTable + ":" + card.FlavorKey, display.FlavorText);
        }

        [Test]
        public void CharacteristicDisplay_ResolvesItsNameFromTheSharedTable()
        {
            // Shared, not Laws-owned: Cities and Random Occurrences need the same strings.
            var data = _presenter.GetCharacteristicDisplay(CharacteristicType.Army);
            Assert.AreEqual("SharedTable:characteristic.army", data.Name);
        }

        [Test]
        public void CharacteristicDisplay_CarriesTheIconFromTheSharedRegistry()
        {
            var texture = new Texture2D(4, 4);
            _createdObjects.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);
            _createdObjects.Add(sprite);

            // Rebuild against a registry where exactly one characteristic has art, so this
            // proves the icon is looked up per row rather than shared across every bar.
            var definitions = new List<CharacteristicDefinition>();
            foreach (CharacteristicType type in Enum.GetValues(typeof(CharacteristicType)))
                definitions.Add(TestCharacteristics.Definition(
                    type, type == CharacteristicType.Science ? sprite : null, _createdObjects));

            _presenter.Dispose();
            _presenter = new LawsPresenter(
                _manager, _ledger, _eventBus, _audio, _haptics, _localization,
                TestCharacteristics.Registry(definitions, _createdObjects), _popups);

            Assert.That(_presenter.GetCharacteristicDisplay(CharacteristicType.Science).Icon,
                Is.SameAs(sprite));
            Assert.That(_presenter.GetCharacteristicDisplay(CharacteristicType.Army).Icon,
                Is.Null, "A characteristic with no art must not inherit another's icon.");
        }

        /// <summary>
        /// A law must not reveal what it does before the player commits (GDD §6) — inferring
        /// it from the writing is the mechanic. This guards the shape of the display struct,
        /// not just today's values: every field on it is rendered *before* the swipe, so a
        /// re-added summary would silently give the answer away.
        /// </summary>
        [Test]
        public void CardDisplay_RevealsNothingAboutItsEffects_BeforeTheSwipe()
        {
            PushOneCardToHeld("card_x", accept: new[]
            {
                new LawCardEffect(CharacteristicType.Army, 10f)
            });

            Assert.IsTrue(_presenter.TryGetActiveCardDisplay(out var display));

            var fields = typeof(LawCardDisplayData)
                .GetFields(System.Reflection.BindingFlags.Public |
                           System.Reflection.BindingFlags.Instance)
                .Select(f => f.Name)
                .ToArray();

            Assert.That(fields, Is.EquivalentTo(new[] { "CardId", "Title", "FlavorText" }),
                "A new field on LawCardDisplayData is shown before the player decides. If it " +
                "names a characteristic or a point value, the guess is no longer a guess.");

            foreach (var text in new[] { display.Title, display.FlavorText })
            {
                StringAssert.DoesNotContain("army", text.ToLowerInvariant());
                StringAssert.DoesNotContain("10", text);
            }
        }

        /// <summary>
        /// LocalizeStringEvent components refresh themselves; Presenter-built strings do not.
        /// Without this the chrome would switch language and every card title would not.
        /// </summary>
        [Test]
        public void LocaleChange_TriggersAFullRefresh()
        {
            PushOneCardToHeld();

            bool refreshed = false;
            _presenter.OnStateChanged += () => refreshed = true;

            _localization.RaiseLocaleChanged();

            Assert.IsTrue(refreshed, "A locale change must re-render everything the Presenter resolved.");
        }

        [Test]
        public void Dispose_StopsListeningForLocaleChanges()
        {
            bool refreshed = false;
            _presenter.OnStateChanged += () => refreshed = true;

            _presenter.Dispose();
            _localization.RaiseLocaleChanged();

            Assert.IsFalse(refreshed, "A disposed Presenter must not be kept alive by the locale event.");

            // TearDown disposes again; that must stay safe.
            _characteristics = TestCharacteristics.Complete(_createdObjects);
            _presenter = new LawsPresenter(
                _manager, _ledger, _eventBus, _audio, _haptics, _localization, _characteristics, _popups);
        }

        [Test]
        public void AvailableCardCount_WithOneActiveAndNoReady_IsOne()
        {
            PushOneCardToHeld();
            // 1 active + 0 ready + 7 replenishing = the "1/8" readout
            Assert.AreEqual(1, _presenter.AvailableCardCount);
            Assert.AreEqual(8, _presenter.MaxCardCount);
        }

        [Test]
        public void AvailableCardCount_CountsTheActiveCardAndReadyOnes()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();

            // Advance 3 intervals: 1 active, 2 ready, 5 replenishing → "3/8".
            _clock.Advance(TimeSpan.FromSeconds(360));
            _manager.ProcessReplenishment();

            Assert.AreEqual(3, _presenter.AvailableCardCount);
        }

        [Test]
        public void AvailableCardCount_PlusReplenishing_AlwaysEqualsTheCap()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();

            // Check the invariant holds at several points along the refill.
            for (int step = 0; step < 5; step++)
            {
                Assert.AreEqual(_presenter.MaxCardCount,
                    _presenter.AvailableCardCount + _manager.CardsReplenishing,
                    "available + replenishing must always equal the cap.");
                _clock.Advance(TimeSpan.FromSeconds(120));
                _manager.ProcessReplenishment();
            }
        }

        // ── IsWaiting / IsReplenishing ────────────────────────────────────────────

        [Test]
        public void IsWaiting_FreshManagerWithNoCards_ReturnsTrue()
        {
            Assert.IsTrue(_presenter.IsWaiting);
        }

        [Test]
        public void IsReplenishing_AfterInitializeCardPool_ReturnsTrue()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();
            Assert.IsTrue(_presenter.IsReplenishing);
        }

        [Test]
        public void IsWaiting_AfterCardArrives_ReturnsFalse()
        {
            PushOneCardToHeld();
            Assert.IsFalse(_presenter.IsWaiting);
        }

        // ── OnSwipeAccepted ───────────────────────────────────────────────────────

        [Test]
        public void OnSwipeAccepted_WithNoActiveCard_ReturnsFalse()
        {
            Assert.IsFalse(_presenter.OnSwipeAccepted());
        }

        [Test]
        public void OnSwipeAccepted_AppliesAcceptEffectAndReturnsTrue()
        {
            var effect = new LawCardEffect(CharacteristicType.Army, 10f);
            PushOneCardToHeld(accept: new[] { effect });

            float before = _ledger.GetCharacteristic(CharacteristicType.Army).PointsIntoCurrentLevel;
            bool result  = _presenter.OnSwipeAccepted();

            Assert.IsTrue(result);
            Assert.AreEqual(before + 10f,
                _ledger.GetCharacteristic(CharacteristicType.Army).PointsIntoCurrentLevel);
        }

        [Test]
        public void OnSwipeAccepted_PlaysAcceptSfxAndTriggersHaptic()
        {
            PushOneCardToHeld();
            _audio.PlayedSfxIds.Clear();
            _haptics.LightTriggerCount = 0;

            _presenter.OnSwipeAccepted();

            Assert.Contains("sfx_laws_card_accept", _audio.PlayedSfxIds);
            Assert.AreEqual(1, _haptics.LightTriggerCount);
        }

        [Test]
        public void OnSwipeAccepted_FiresOnStateChanged()
        {
            PushOneCardToHeld();
            bool fired = false;
            _presenter.OnStateChanged += () => fired = true;

            _presenter.OnSwipeAccepted();

            Assert.IsTrue(fired);
        }

        // ── OnSwipeRejected ───────────────────────────────────────────────────────

        [Test]
        public void OnSwipeRejected_AppliesRejectEffectAndReturnsTrue()
        {
            var effect = new LawCardEffect(CharacteristicType.Education, 5f);
            PushOneCardToHeld(reject: new[] { effect });

            float before = _ledger.GetCharacteristic(CharacteristicType.Education).PointsIntoCurrentLevel;
            bool result  = _presenter.OnSwipeRejected();

            Assert.IsTrue(result);
            Assert.AreEqual(before + 5f,
                _ledger.GetCharacteristic(CharacteristicType.Education).PointsIntoCurrentLevel);
        }

        [Test]
        public void OnSwipeRejected_PlaysRejectSfxAndTriggersHaptic()
        {
            PushOneCardToHeld();
            _audio.PlayedSfxIds.Clear();
            _haptics.LightTriggerCount = 0;

            _presenter.OnSwipeRejected();

            Assert.Contains("sfx_laws_card_reject", _audio.PlayedSfxIds);
            Assert.AreEqual(1, _haptics.LightTriggerCount);
        }

        // ── OnCrystalRefillRequested ──────────────────────────────────────────────

        /// <summary>
        /// Regression: the refill button's return value used to mean "the refill happened",
        /// and the View replayed the card's entrance animation on it. Once the button started
        /// opening a confirmation instead, merely *opening* the dialog replayed the animation
        /// behind it. The entrance is now driven by a card actually arriving.
        /// </summary>
        [Test]
        public void OpeningTheRefillConfirm_DoesNotAnnounceACardArrival()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();
            _ledger.AddCrystals(100);

            int arrivals = 0;
            _presenter.OnCardArrived += () => arrivals++;

            // The fixture's PopupSystem has no registry, so opening the popup throws — loudly,
            // as a setup mistake should. Expect that rather than let it fail the run.
            LogAssert.Expect(LogType.Exception, new Regex("No popup registry"));
            _presenter.OnCrystalRefillRequested().Forget();

            Assert.AreEqual(0, arrivals,
                "Opening the dialog must not look like a card turning up.");
            Assert.AreEqual(100, _ledger.Crystals,
                "A popup that could not be shown must never fall through into the purchase.");
        }

        /// <summary>
        /// A card that arrives on its own — nobody pressed anything — still animates.
        /// The old button-driven approach never covered this at all.
        /// </summary>
        [Test]
        public void ACardMaturingOnItsOwn_AnnouncesAnArrival()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();

            int arrivals = 0;
            _presenter.OnCardArrived += () => arrivals++;

            _clock.Advance(TimeSpan.FromSeconds(_config.CardReplenishTimeSeconds + 1));
            _manager.ProcessReplenishment();

            Assert.AreEqual(1, arrivals);
        }

        [Test]
        public void OnCrystalRefillRequested_WithoutEnoughCrystals_OpensNothingAndSpendsNothing()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();

            _ledger.AddCrystals(1);
            int before = _ledger.Crystals;

            // Returns UniTaskVoid now; the guard runs before anything is awaited, so this
            // completes synchronously without touching the popup manager.
            _presenter.OnCrystalRefillRequested().Forget();

            Assert.IsFalse(_presenter.CanAffordRefill);
            Assert.AreEqual(before, _ledger.Crystals, "Nothing may be spent.");
        }

        // ── OnBuyUpRequested ──────────────────────────────────────────────────────

        [Test]
        public void OnBuyUpRequested_WithEnoughCrystals_LevelsUpAndReturnsTrue()
        {
            _ledger.AddCrystals(100);
            int levelBefore = _ledger.GetCharacteristic(CharacteristicType.Medicine).Level;

            bool result = _presenter.OnBuyUpRequested(CharacteristicType.Medicine);

            Assert.IsTrue(result);
            Assert.AreEqual(levelBefore + 1,
                _ledger.GetCharacteristic(CharacteristicType.Medicine).Level);
        }

        [Test]
        public void OnBuyUpRequested_WithoutEnoughCrystals_ReturnsFalse()
        {
            Assert.IsFalse(_presenter.OnBuyUpRequested(CharacteristicType.Science));
        }

        // ── GetCharacteristicDisplay ──────────────────────────────────────────────

        [Test]
        public void GetCharacteristicDisplay_FreshState_ReturnsZeroProgress()
        {
            var data = _presenter.GetCharacteristicDisplay(CharacteristicType.Welfare);
            Assert.AreEqual(1, data.Level);
            Assert.AreEqual(0f, data.ProgressFraction, 0.001f);
        }

        [Test]
        public void GetCharacteristicDisplay_AfterPartialPoints_ReturnsCorrectFraction()
        {
            // Add 50 points to a 100-point level → 50% progress.
            _ledger.AddCharacteristicPoints(CharacteristicType.Infrastructure, 50f);

            var data = _presenter.GetCharacteristicDisplay(CharacteristicType.Infrastructure);
            Assert.AreEqual(0.5f, data.ProgressFraction, 0.001f);
        }

        [Test]
        public void GetCharacteristicDisplay_BuyUpCost_MatchesCalculator()
        {
            // Level 1, 0 points → remaining = 100 → ceil(100/20) = 5
            var data = _presenter.GetCharacteristicDisplay(CharacteristicType.Army);
            Assert.AreEqual(5, data.BuyUpCost);
        }

        [Test]
        public void GetCharacteristicDisplay_CanAffordBuyUp_TrueWhenEnoughCrystals()
        {
            _ledger.AddCrystals(5);
            var data = _presenter.GetCharacteristicDisplay(CharacteristicType.Army);
            Assert.IsTrue(data.CanAffordBuyUp);
        }

        [Test]
        public void GetCharacteristicDisplay_CanAffordBuyUp_FalseWhenBroke()
        {
            var data = _presenter.GetCharacteristicDisplay(CharacteristicType.Army);
            Assert.IsFalse(data.CanAffordBuyUp);
        }

        // ── CharacteristicLeveledUp event ─────────────────────────────────────────

        [Test]
        public void CharacteristicLeveledUp_EventBus_TriggersAudioAndHapticAndPresenterEvent()
        {
            CharacteristicType? receivedType = null;
            _presenter.OnCharacteristicLeveledUp += t => receivedType = t;
            _audio.PlayedSfxIds.Clear();
            _haptics.LightTriggerCount = 0;

            _ledger.AddCrystals(100);
            _presenter.OnBuyUpRequested(CharacteristicType.Welfare);

            Assert.AreEqual(CharacteristicType.Welfare, receivedType);
            Assert.Contains("sfx_laws_level_up", _audio.PlayedSfxIds);
            Assert.GreaterOrEqual(_haptics.LightTriggerCount, 1);
        }

        // ── Tick / replenishment integration ─────────────────────────────────────

        [Test]
        public void CardArrives_WhileScreenVisible_FiresOnStateChangedAndPlaysArriveSfx()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();
            _presenter.SetScreenVisible(true);

            bool fired = false;
            _presenter.OnStateChanged += () => fired = true;
            _audio.PlayedSfxIds.Clear();

            _clock.Advance(TimeSpan.FromSeconds(120));
            _manager.ProcessReplenishment();

            Assert.IsTrue(fired);
            Assert.Contains("sfx_laws_card_arrive", _audio.PlayedSfxIds);
        }

        [Test]
        public void NoCardArrives_DoesNotFireOnStateChanged()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();

            bool fired = false;
            _presenter.OnStateChanged += () => fired = true;

            _clock.Advance(TimeSpan.FromSeconds(10)); // not enough
            _manager.ProcessReplenishment();

            Assert.IsFalse(fired);
        }

        // ── Screen visibility gating ──────────────────────────────────────────────

        /// <summary>
        /// The queue keeps running on other tabs, but a card arriving on a screen nobody
        /// is looking at must not make a noise.
        /// </summary>
        [Test]
        public void CardArrives_WhileScreenHidden_UpdatesStateButStaysSilent()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();
            _presenter.SetScreenVisible(false);

            bool fired = false;
            _presenter.OnStateChanged += () => fired = true;
            _audio.PlayedSfxIds.Clear();

            _clock.Advance(TimeSpan.FromSeconds(120));
            _manager.ProcessReplenishment();

            Assert.IsTrue(fired, "State must still update so the screen is right on return.");
            Assert.IsTrue(_presenter.HasActiveCard, "The card really did arrive.");
            CollectionAssert.DoesNotContain(_audio.PlayedSfxIds, "sfx_laws_card_arrive");
        }

        [Test]
        public void SetScreenVisible_OnShow_RefreshesSoTheViewCatchesUp()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();

            // Queue advances while hidden.
            _presenter.SetScreenVisible(false);
            _clock.Advance(TimeSpan.FromSeconds(120));
            _manager.ProcessReplenishment();

            bool fired = false;
            _presenter.OnStateChanged += () => fired = true;

            _presenter.SetScreenVisible(true);

            Assert.IsTrue(fired, "Becoming visible must trigger a re-render.");
        }

        [Test]
        public void SetScreenVisible_RepeatedSameValue_DoesNotRefreshAgain()
        {
            _presenter.SetScreenVisible(true);

            int refreshes = 0;
            _presenter.OnStateChanged += () => refreshes++;

            _presenter.SetScreenVisible(true);
            _presenter.SetScreenVisible(true);

            Assert.AreEqual(0, refreshes);
        }

        /// <summary>
        /// The whole point of Task 5: the queue advances with no View and no Presenter
        /// tick in the picture at all.
        /// </summary>
        [Test]
        public void QueueAdvances_WithoutAnyPresenterInvolvement()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();

            _clock.Advance(TimeSpan.FromSeconds(120));
            _manager.ProcessReplenishment();

            Assert.IsTrue(_manager.HasActiveCard);
            Assert.IsTrue(_presenter.TryGetActiveCardDisplay(out var display));
            Assert.AreEqual(card.CardId, display.CardId);
        }

        // ── Price shown == price charged ──────────────────────────────────────────
        // The Presenter used to derive display prices from LawsConfig itself, duplicating
        // the formulas LawsManager charges with. Identical today, free to drift tomorrow —
        // and in a system where crystals cost real money, quoting one price and taking
        // another is the worst kind of bug to ship.

        [Test]
        public void BuyUpCost_ShownMatchesCrystalsActuallyTaken()
        {
            _ledger.AddCrystals(1000);

            int quoted = _presenter.GetCharacteristicDisplay(CharacteristicType.Army).BuyUpCost;
            int before = _ledger.Crystals;

            Assert.IsTrue(_presenter.OnBuyUpRequested(CharacteristicType.Army));

            Assert.AreEqual(quoted, before - _ledger.Crystals,
                "The buy-up charged a different number of crystals than it displayed.");
        }

        [Test]
        public void BuyUpCost_ShownMatchesCharged_AtEveryLevel()
        {
            _ledger.AddCrystals(100_000);

            // Levels get more expensive, so walk several and check each transaction.
            for (int level = 1; level <= 6; level++)
            {
                int quoted = _presenter.GetCharacteristicDisplay(CharacteristicType.Science).BuyUpCost;
                int before = _ledger.Crystals;

                Assert.IsTrue(_presenter.OnBuyUpRequested(CharacteristicType.Science));
                Assert.AreEqual(quoted, before - _ledger.Crystals, $"Mismatch at level {level}.");
            }
        }

        // ── RefillCost / CanAffordRefill ──────────────────────────────────────────

        [Test]
        public void RefillCost_AllSlotsReplenishing_IsReplenishingCountTimesCostPerCard()
        {
            // After InitializeCardPool with no prior cards: 0 held, 8 replenishing.
            // But we haven't initialized yet so CardsReplenishing = 0 → RefillCost = 0.
            // Initialize and check:
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool(); // 8 replenishing × 2 = 16

            Assert.AreEqual(16, _presenter.RefillCost);
        }

        /// <summary>
        /// The monetization invariant: the number the UI would show is the number actually
        /// taken. It used to be asserted through the confirm dialog; with the popup system
        /// being rebuilt it is pinned directly against the Manager, which is where the charge
        /// lives. Restore the dialog-level version once the new popup flow is wired.
        /// </summary>
        [Test]
        public void RefillCost_ShownMatchesCrystalsActuallyTaken()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();
            _ledger.AddCrystals(500);

            int quoted = _presenter.RefillCost;
            Assert.Greater(quoted, 0, "There should be something to buy.");

            int before = _ledger.Crystals;
            Assert.IsTrue(_manager.RefillWithCrystals());

            Assert.AreEqual(quoted, before - _ledger.Crystals,
                "Crystals taken must equal the price the player was quoted.");
        }

        [Test]
        public void CanAffordRefill_WhenNoSlotsReplenishing_ReturnsFalse()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();

            // Fill all slots so cardsReplenishing = 0.
            _clock.Advance(TimeSpan.FromSeconds(1200));
            _manager.ProcessReplenishment();

            _ledger.AddCrystals(100);
            Assert.IsFalse(_presenter.CanAffordRefill);
        }

        [Test]
        public void CanAffordRefill_NotEnoughCrystals_ReturnsFalse()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool(); // 8 replenishing → cost 16

            // 0 crystals
            Assert.IsFalse(_presenter.CanAffordRefill);
        }

        [Test]
        public void CanAffordRefill_EnoughCrystals_ReturnsTrue()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool(); // 8 replenishing → cost 16

            _ledger.AddCrystals(16);
            Assert.IsTrue(_presenter.CanAffordRefill);
        }
    }
}
