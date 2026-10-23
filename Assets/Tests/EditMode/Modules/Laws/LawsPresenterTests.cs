using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using KingdomRuler.Systems.Events;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Systems.Popups;
using KingdomRuler.Modules.Laws;
using KingdomRuler.Modules.Laws.UI;
using KingdomRuler.Tests.EditMode.Systems;
using KingdomRuler.Tests.EditMode.Systems.Ledger;

namespace KingdomRuler.Tests.EditMode.Modules.Laws
{
    [TestFixture]
    public sealed class LawsPresenterTests
    {
        private FakeClock               _clock;
        private EventBus                _eventBus;
        private KingdomLedger           _ledger;
        private LawsConfig              _config;
        private LawsManager             _manager;
        private FakeAudioService        _audio;
        private FakeHapticService       _haptics;
        private FakeLocalizationService _localization;
        private PopupSystem             _popups;
        private LawsPresenter           _presenter;

        private readonly List<UnityEngine.Object> _created = new();

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
            _config.AllCards                 = new[] { Card("card") };
            _created.Add(_config);

            _manager      = new LawsManager(_ledger, _clock, _config);
            _audio        = new FakeAudioService();
            _haptics      = new FakeHapticService();
            _localization = new FakeLocalizationService();

            // No popup registry: opening a popup throws, so nothing is ever confirmed here.
            // Confirmed refills are verified in Play mode.
            var popupHost = new GameObject("popups");
            _created.Add(popupHost);
            _popups = popupHost.AddComponent<PopupSystem>();

            _presenter = NewPresenter(TestCharacteristics.Complete(_created));
        }

        [TearDown]
        public void TearDown()
        {
            _presenter.Dispose();
            foreach (var obj in _created)
                if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            _created.Clear();
        }

        private LawsPresenter NewPresenter(CharacteristicRegistry characteristics) =>
            new LawsPresenter(_manager, _ledger, _eventBus, _audio, _haptics, _localization, characteristics, _popups);

        private LawCardDefinition Card(string id, LawCardEffect[] accept = null, LawCardEffect[] reject = null)
        {
            var card = ScriptableObject.CreateInstance<LawCardDefinition>();
            card.CardId        = id;
            card.AcceptEffects = accept;
            card.RejectEffects = reject;
            _created.Add(card);
            return card;
        }

        private void Wait(int intervals)
        {
            _clock.Advance(TimeSpan.FromSeconds(120 * intervals));
            _manager.AdvanceTo(_clock.UtcNow);
        }

        /// <summary>Start a game with one card and wait until it is shown.</summary>
        private LawCardDefinition ShowCard(string id = "card", LawCardEffect[] accept = null, LawCardEffect[] reject = null)
        {
            var card = Card(id, accept, reject);
            _config.AllCards = new[] { card };
            _manager.InitializeNewGame();
            Wait(1);
            return card;
        }

        // ── The active card ───────────────────────────────────────────────────────

        [Test]
        public void ActiveCard_WhenNoneIsShown_IsUnavailable()
        {
            Assert.IsFalse(_presenter.HasActiveCard);
            Assert.IsFalse(_presenter.TryGetActiveCard(out _));
        }

        [Test]
        public void ActiveCard_ResolvesItsTextThroughTheDefinitionsOwnKeys()
        {
            var card = ShowCard("law_conscription");

            Assert.IsTrue(_presenter.TryGetActiveCard(out var view));
            Assert.AreEqual(LawCardDefinition.StringTable + ":" + card.TitleKey,  view.Title);
            Assert.AreEqual(LawCardDefinition.StringTable + ":" + card.FlavorKey, view.FlavorText);
            Assert.AreEqual("LawCardsTable:law_conscription.title", view.Title, "Pins the derivation itself.");
        }

        /// <summary>
        /// Everything on the card is shown before the player decides, so a field naming a
        /// characteristic or a value would give the answer away (GDD §6). Guards the shape, not just the values.
        /// </summary>
        [Test]
        public void ActiveCard_RevealsNothingAboutItsEffects()
        {
            ShowCard("card_x", accept: new[] { new LawCardEffect(CharacteristicType.Army, 10f) });
            Assert.IsTrue(_presenter.TryGetActiveCard(out var view));

            var fields = typeof(LawCardViewData)
                .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Select(f => f.Name);
            Assert.That(fields, Is.EquivalentTo(new[] { "Title", "FlavorText" }),
                "A new field on LawCardViewData is shown before the player decides.");

            foreach (var text in new[] { view.Title, view.FlavorText })
            {
                StringAssert.DoesNotContain("army", text.ToLowerInvariant());
                StringAssert.DoesNotContain("10", text);
            }
        }

        // ── Queue readout ─────────────────────────────────────────────────────────

        [Test]
        public void HeldCardCount_CountsTheShownCardAndThoseBehindIt()
        {
            _manager.InitializeNewGame();
            Wait(3);

            Assert.AreEqual(3, _presenter.HeldCardCount);
            Assert.AreEqual(8, _presenter.MaxCardCount);
        }

        [Test]
        public void IsWaiting_OnlyWhenNothingIsShownOrOnTheWay()
        {
            Assert.IsTrue(_presenter.IsWaiting, "Nothing started yet.");

            _manager.InitializeNewGame();
            Assert.IsFalse(_presenter.IsWaiting);
            Assert.IsTrue(_presenter.IsReplenishing);

            Wait(1);
            Assert.IsFalse(_presenter.IsWaiting);
        }

        // ── Dials ─────────────────────────────────────────────────────────────────

        [Test]
        public void Dial_FreshCharacteristic_IsLevelOneWithNoProgress()
        {
            var dial = _presenter.GetDial(CharacteristicType.Welfare);

            Assert.AreEqual(CharacteristicType.Welfare, dial.Type);
            Assert.AreEqual(1, dial.Level);
            Assert.AreEqual(0f, dial.Progress, 0.001f);
        }

        [Test]
        public void Dial_ProgressIsTheFractionOfTheCurrentLevel()
        {
            _ledger.AddCharacteristicPoints(CharacteristicType.Infrastructure, 50f);   // of 100
            Assert.AreEqual(0.5f, _presenter.GetDial(CharacteristicType.Infrastructure).Progress, 0.001f);
        }

        [Test]
        public void Dial_CarriesItsOwnIconFromTheSharedRegistry()
        {
            var texture = new Texture2D(4, 4);
            _created.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);
            _created.Add(sprite);

            var definitions = new List<CharacteristicDefinition>();
            foreach (CharacteristicType type in Enum.GetValues(typeof(CharacteristicType)))
                definitions.Add(TestCharacteristics.Definition(
                    type, type == CharacteristicType.Science ? sprite : null, _created));

            _presenter.Dispose();
            _presenter = NewPresenter(TestCharacteristics.Registry(definitions, _created));

            Assert.That(_presenter.GetDial(CharacteristicType.Science).Icon, Is.SameAs(sprite));
            Assert.That(_presenter.GetDial(CharacteristicType.Army).Icon, Is.Null,
                "A characteristic with no art must not inherit another's icon.");
        }

        // ── Resolving a card ──────────────────────────────────────────────────────

        [Test]
        public void ResolveCard_WithNoCardShown_DoesNothing()
        {
            Assert.IsFalse(_presenter.ResolveCard(LawDecision.Accept));
            CollectionAssert.IsEmpty(_audio.PlayedSfxIds);
        }

        [Test]
        public void ResolveCard_AppliesTheDecision()
        {
            ShowCard(reject: new[] { new LawCardEffect(CharacteristicType.Education, 5f) });

            Assert.IsTrue(_presenter.ResolveCard(LawDecision.Reject));
            Assert.AreEqual(5f, _ledger.GetCharacteristic(CharacteristicType.Education).PointsIntoCurrentLevel);
        }

        [TestCase(LawDecision.Accept, "sfx_laws_card_accept")]
        [TestCase(LawDecision.Reject, "sfx_laws_card_reject")]
        public void ResolveCard_PlaysItsSoundAndHaptic(LawDecision decision, string sfx)
        {
            ShowCard();
            _audio.PlayedSfxIds.Clear();
            _haptics.LightTriggerCount = 0;

            _presenter.ResolveCard(decision);

            Assert.Contains(sfx, _audio.PlayedSfxIds);
            Assert.AreEqual(1, _haptics.LightTriggerCount);
        }

        [Test]
        public void ResolveCard_RaisesStateChanged()
        {
            ShowCard();
            bool raised = false;
            _presenter.StateChanged += () => raised = true;

            _presenter.ResolveCard(LawDecision.Accept);

            Assert.IsTrue(raised);
        }

        // ── Card arrival ──────────────────────────────────────────────────────────

        [Test]
        public void ACardMaturing_AnnouncesAnArrival()
        {
            _manager.InitializeNewGame();
            int arrivals = 0;
            _presenter.CardArrived += () => arrivals++;

            Wait(1);

            Assert.AreEqual(1, arrivals);
        }

        [Test]
        public void ACardArriving_OnAVisibleScreen_PlaysTheArrivalSound()
        {
            _manager.InitializeNewGame();
            _presenter.SetScreenVisible(true);
            bool raised = false;
            _presenter.StateChanged += () => raised = true;

            Wait(1);

            Assert.IsTrue(raised);
            Assert.Contains("sfx_laws_card_arrive", _audio.PlayedSfxIds);
        }

        [Test]
        public void ACardArriving_OnAHiddenScreen_UpdatesStateButStaysSilent()
        {
            _manager.InitializeNewGame();
            _presenter.SetScreenVisible(false);
            bool raised = false;
            _presenter.StateChanged += () => raised = true;

            Wait(1);

            Assert.IsTrue(raised, "The screen must be right when the player returns.");
            Assert.IsTrue(_presenter.HasActiveCard);
            CollectionAssert.DoesNotContain(_audio.PlayedSfxIds, "sfx_laws_card_arrive");
        }

        [Test]
        public void NothingMaturing_RaisesNothing()
        {
            _manager.InitializeNewGame();
            bool raised = false;
            _presenter.StateChanged += () => raised = true;

            _clock.Advance(TimeSpan.FromSeconds(10));
            _manager.AdvanceTo(_clock.UtcNow);

            Assert.IsFalse(raised);
        }

        // ── Screen visibility ─────────────────────────────────────────────────────

        [Test]
        public void BecomingVisible_RefreshesSoTheViewCatchesUp()
        {
            _presenter.SetScreenVisible(false);
            bool raised = false;
            _presenter.StateChanged += () => raised = true;

            _presenter.SetScreenVisible(true);

            Assert.IsTrue(raised);
        }

        [Test]
        public void SetScreenVisible_WithTheSameValue_DoesNotRefreshAgain()
        {
            _presenter.SetScreenVisible(true);
            int refreshes = 0;
            _presenter.StateChanged += () => refreshes++;

            _presenter.SetScreenVisible(true);

            Assert.AreEqual(0, refreshes);
        }

        // ── Refill ────────────────────────────────────────────────────────────────

        [Test]
        public void RefillPrice_IsTheManagersPrice()
        {
            _manager.InitializeNewGame();
            _ledger.AddCrystals(16);

            Assert.AreEqual(_manager.RefillCost, _presenter.RefillCost);
            Assert.IsTrue(_presenter.CanAffordRefill);
        }

        [Test]
        public void RequestRefill_WithoutEnoughCrystals_OpensNothingAndSpendsNothing()
        {
            _manager.InitializeNewGame();
            _ledger.AddCrystals(1);

            // Returns before anything is awaited, so no popup is touched.
            _presenter.RequestRefill().Forget();

            Assert.AreEqual(1, _ledger.Crystals);
        }

        [Test]
        public void OpeningTheRefillConfirmation_NeitherSpendsNorAnnouncesACard()
        {
            _manager.InitializeNewGame();
            _ledger.AddCrystals(100);
            int arrivals = 0;
            _presenter.CardArrived += () => arrivals++;

            LogAssert.Expect(LogType.Exception, new Regex("No popup registry"));
            _presenter.RequestRefill().Forget();

            Assert.AreEqual(0, arrivals);
            Assert.AreEqual(100, _ledger.Crystals, "A popup that could not be shown must never fall through into the purchase.");
        }

        // ── Buy-up ────────────────────────────────────────────────────────────────

        [Test]
        public void BuyUp_ChargesTheQuotedPrice()
        {
            _ledger.AddCrystals(1000);
            int quoted = _presenter.BuyUpCost(CharacteristicType.Army);

            Assert.IsTrue(_presenter.CanAffordBuyUp(CharacteristicType.Army));
            Assert.IsTrue(_presenter.BuyUp(CharacteristicType.Army));
            Assert.AreEqual(1000 - quoted, _ledger.Crystals);
        }

        [Test]
        public void BuyUp_WithoutEnoughCrystals_IsRefused()
        {
            Assert.IsFalse(_presenter.CanAffordBuyUp(CharacteristicType.Science));
            Assert.IsFalse(_presenter.BuyUp(CharacteristicType.Science));
        }

        [Test]
        public void ALevelUp_PlaysItsFeelHooksAndNamesTheCharacteristic()
        {
            CharacteristicType? leveled = null;
            _presenter.CharacteristicLeveledUp += t => leveled = t;
            _ledger.AddCrystals(100);

            _presenter.BuyUp(CharacteristicType.Welfare);

            Assert.AreEqual(CharacteristicType.Welfare, leveled);
            Assert.Contains("sfx_laws_level_up", _audio.PlayedSfxIds);
            Assert.GreaterOrEqual(_haptics.LightTriggerCount, 1);
        }

        // ── Localization ──────────────────────────────────────────────────────────

        /// <summary>Presenter-built strings do not refresh themselves the way prefab text does.</summary>
        [Test]
        public void ALocaleChange_TriggersAFullRefresh()
        {
            bool raised = false;
            _presenter.StateChanged += () => raised = true;

            _localization.RaiseLocaleChanged();

            Assert.IsTrue(raised);
        }

        [Test]
        public void Dispose_StopsListening()
        {
            bool raised = false;
            _presenter.StateChanged += () => raised = true;

            _presenter.Dispose();
            _localization.RaiseLocaleChanged();
            _manager.InitializeNewGame();
            Wait(1);

            Assert.IsFalse(raised);
        }
    }
}
