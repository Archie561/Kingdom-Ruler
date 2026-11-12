using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using KingdomRuler.Modules.Economy;
using KingdomRuler.Modules.Economy.UI;
using KingdomRuler.Shared.Text;
using KingdomRuler.Systems.Events;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Systems.Popups;
using KingdomRuler.Tests.EditMode.Systems;

namespace KingdomRuler.Tests.EditMode.Modules.Economy
{
    /// <summary>
    /// View data, the buy-on-tap and collect paths with their feel hooks, and visibility gating.
    /// </summary>
    /// <remarks>
    /// A gold purchase opens no popup, so it is tested here end to end. The crystal top-up waits on a
    /// confirmation, which EditMode cannot answer (no popup registry, so opening one throws); it is
    /// verified in Play mode (<c>ARCHITECTURE.md</c> §8), as Trade's upgrades are.
    /// </remarks>
    [TestFixture]
    public sealed class EconomyPresenterTests
    {
        private const string Bakery = "business.bakery";
        private const string Tavern = "business.tavern";

        private readonly List<UnityEngine.Object> _created = new();

        private FakeClock               _clock;
        private KingdomLedger           _ledger;
        private EconomyManager          _manager;
        private FakeAudioService        _audio;
        private FakeHapticService       _haptics;
        private FakeLocalizationService _localization;
        private PopupSystem             _popups;
        private EconomyPresenter        _presenter;
        private CultureInfo             _culture;

        [SetUp]
        public void SetUp()
        {
            // Numbers are formatted in the current culture; pin it so "12.3K" reads the same on every machine.
            _culture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

            _clock   = new FakeClock();
            _ledger  = new KingdomLedger(new EventBus());
            _manager = new EconomyManager(_ledger, _clock,
                Config(Business(Bakery, price: 100, perHour: 60), Business(Tavern, price: 1_500, perHour: 1_000)));

            _audio        = new FakeAudioService();
            _haptics      = new FakeHapticService();
            _localization = new FakeLocalizationService();
            UseEnglishSuffixes();

            var popupHost = new GameObject("popups");
            _created.Add(popupHost);
            _popups = popupHost.AddComponent<PopupSystem>();

            _presenter = new EconomyPresenter(_manager, _ledger, _audio, _haptics, _localization, _popups);
        }

        [TearDown]
        public void TearDown()
        {
            _presenter.Dispose();
            foreach (var obj in _created)
                if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            _created.Clear();
            CultureInfo.CurrentCulture = _culture;
        }

        // ── Rows ──────────────────────────────────────────────────────────────────

        [Test]
        public void BusinessIds_AreEveryBusiness_InDisplayOrder()
        {
            CollectionAssert.AreEqual(new[] { Bakery, Tavern }, _presenter.BusinessIds);
        }

        [Test]
        public void GetRow_CarriesTheStorageNumbers()
        {
            Buy(Bakery, 3);
            Elapse(TimeSpan.FromHours(1));

            var row = _presenter.GetRow(Bakery);

            Assert.AreEqual(3, row.UnitsOwned);
            Assert.AreEqual(180, row.Stored);
            Assert.AreEqual(4_320, row.Capacity);
            Assert.IsTrue(row.CanCollect, "The row's Collect button shows.");
            Assert.IsFalse(row.IsFull);
            Assert.AreEqual(180f / 4_320f, row.Fill, 1e-6f);
        }

        [Test]
        public void GetRow_AnEmptyOrUnownedStorage_HasNothingToCollect()
        {
            Assert.IsFalse(_presenter.GetRow(Bakery).CanCollect);
            Assert.AreEqual(0f, _presenter.GetRow(Bakery).Fill);
        }

        [Test]
        public void GetRow_IsFull_WhenTheStorageIs()
        {
            Buy(Bakery, 1);
            Elapse(TimeSpan.FromHours(24));

            Assert.IsTrue(_presenter.GetRow(Bakery).IsFull);
            Assert.AreEqual(1f, _presenter.GetRow(Bakery).Fill);
        }

        /// <summary>It is read every frame: a string on it would be built, and allocated, every frame.</summary>
        [Test]
        public void TheRowData_CarriesNoStrings()
        {
            foreach (var field in typeof(BusinessRowViewData).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                Assert.AreNotEqual(typeof(string), field.FieldType, $"BusinessRowViewData.{field.Name} is a string.");
        }

        [Test]
        public void TheGoldTotal_IsTheBalance()
        {
            _ledger.AddGold(12_345);

            Assert.AreEqual(12_345, _presenter.Gold);
            Assert.AreEqual("12.3K", _presenter.Amount(_presenter.Gold));
        }

        // ── Text ──────────────────────────────────────────────────────────────────

        [Test]
        public void Amount_UsesTheSharedSuffixes_AndRereadsThemWhenTheLanguageChanges()
        {
            Assert.AreEqual("1.23M", _presenter.Amount(1_234_567));

            int changes = 0;
            _presenter.StateChanged += () => changes++;
            _localization.Overrides[NumberSuffixes.StringTable + ":" + NumberSuffixes.MillionKey] = "{0} млн";
            _localization.RaiseLocaleChanged();

            Assert.AreEqual(1, changes);
            Assert.AreEqual("1.23 млн", _presenter.Amount(1_234_567));
        }

        [Test]
        public void SuffixesReadBeforeLocalizationIsReady_AreReplacedWhenItIs()
        {
            // Localization starts asynchronously; anything read before then is a placeholder. The
            // service raises LocaleChanged once when it is ready, which must drop the cached suffixes,
            // or the placeholders would stay for the whole session (Laws.md §6.13).
            _localization.IsReady = false;
            _localization.Overrides.Clear();
            Assert.AreNotEqual("12.3K", _presenter.Amount(12_345), "Placeholder suffixes, before startup.");

            UseEnglishSuffixes();
            _localization.BecomeReady();

            Assert.AreEqual("12.3K", _presenter.Amount(12_345));
        }

        [Test]
        public void Amount_ResolvesTheSuffixesOnce_NotPerNumber()
        {
            _presenter.Amount(12_345);
            int resolved = _localization.Requested.Count;

            for (int i = 0; i < 10; i++) _presenter.Amount(12_345 + i * 1_000);

            Assert.AreEqual(resolved, _localization.Requested.Count);
        }

        [Test]
        public void BusinessName_ComesFromTheBusinessesTable()
        {
            Assert.AreEqual(BusinessDefinition.StringTable + ":business.bakery.name", _presenter.BusinessName(Bakery));
            Assert.AreEqual(string.Empty, _presenter.BusinessName("business.unknown"));
        }

        // ── The panel's data ──────────────────────────────────────────────────────

        [Test]
        public void GetBusiness_DescribesTheBusiness()
        {
            Buy(Bakery, 3);
            Elapse(TimeSpan.FromHours(1));
            _ledger.AddGold(450 - _ledger.Gold);

            var panel = _presenter.GetBusiness(Bakery);

            Assert.AreEqual(BusinessDefinition.StringTable + ":business.bakery.name", panel.Name);
            Assert.AreEqual(BusinessDefinition.StringTable + ":business.bakery.flavor", panel.Flavor);
            Assert.AreEqual(Text(EconomyUIText.UnitsOwned) + "(3)", panel.OwnedText);
            Assert.AreEqual(Text(EconomyUIText.Rate) + "(180)", panel.RateText);
            Assert.AreEqual(180, panel.Stored);
            Assert.AreEqual(4_320, panel.Capacity);
            Assert.AreEqual("180", panel.StoredText);
            Assert.AreEqual("4,320", panel.CapacityText);
            Assert.IsTrue(panel.CanCollect);

            Assert.AreEqual("100", panel.OnePrice);
            Assert.AreEqual("1,000", panel.TenPrice);
            Assert.AreEqual(Text(EconomyUIText.BuyMax) + "(4)", panel.MaxText, "450 gold buys four.");
            Assert.AreEqual("400", panel.MaxPrice);
            Assert.IsTrue(panel.AffordsOne);
            Assert.IsFalse(panel.AffordsTen, "Still tappable — it offers the top-up — only drawn muted.");
            Assert.IsTrue(panel.AffordsMax);
            Assert.IsTrue(panel.CanBuyOne && panel.CanBuyTen && panel.CanBuyMax);
            Assert.IsNull(panel.MaxedText);
        }

        [Test]
        public void GetBusiness_MaxWithNothingAffordable_OffersOne()
        {
            _ledger.AddGold(40);

            var panel = _presenter.GetBusiness(Bakery);

            Assert.AreEqual(Text(EconomyUIText.BuyMax) + "(1)", panel.MaxText);
            Assert.AreEqual("100", panel.MaxPrice);
            Assert.IsFalse(panel.AffordsMax);
            Assert.IsTrue(panel.CanBuyMax, "Tappable: it opens the crystal top-up for one unit.");
        }

        [Test]
        public void GetBusiness_NearTheUnitsCeiling_OnlyTenIsInactive()
        {
            Buy(Bakery, EconomyConfig.MaxUnitsPerType - 5);

            var panel = _presenter.GetBusiness(Bakery);

            Assert.IsTrue(panel.CanBuyOne);
            Assert.IsFalse(panel.CanBuyTen);
            Assert.IsTrue(panel.CanBuyMax);
        }

        [Test]
        public void GetBusiness_AtTheUnitsCeiling_ShowsMaximumOwnedInsteadOfPrices()
        {
            Buy(Bakery, EconomyConfig.MaxUnitsPerType);

            var panel = _presenter.GetBusiness(Bakery);

            Assert.AreEqual(Text(EconomyUIText.Maxed), panel.MaxedText);
            Assert.IsFalse(panel.CanBuyOne || panel.CanBuyTen || panel.CanBuyMax);
            Assert.IsNull(panel.OnePrice);
            Assert.IsNull(panel.TenPrice);
            Assert.IsNull(panel.MaxText);
            Assert.IsNull(panel.MaxPrice);
        }

        [Test]
        public void GetBusiness_ComparesFieldByField()
        {
            // The open panel redraws only when this changes, so an unchanged read must compare equal.
            Buy(Bakery, 1);

            Assert.AreEqual(_presenter.GetBusiness(Bakery), _presenter.GetBusiness(Bakery));

            var before = _presenter.GetBusiness(Bakery);
            Elapse(TimeSpan.FromMinutes(1));
            Assert.AreNotEqual(before, _presenter.GetBusiness(Bakery), "A coin landed.");
        }

        [Test]
        public void ReadingAnything_SpendsNothing()
        {
            _ledger.AddGold(5_000);
            _ledger.AddCrystals(20);

            _presenter.GetBusiness(Bakery);
            _presenter.GetRow(Bakery);
            _presenter.GetBusiness(Tavern);

            Assert.AreEqual(5_000, _ledger.Gold);
            Assert.AreEqual(20, _ledger.Crystals);
            Assert.AreEqual(0, _manager.UnitsOwned(Bakery));
        }

        // ── Collecting ────────────────────────────────────────────────────────────

        [Test]
        public void Collect_MovesTheGold_WithOneSound_AndNoHaptic()
        {
            Buy(Bakery, 1);
            Elapse(TimeSpan.FromHours(1));
            string collectedId = null;
            long collectedAmount = 0;
            _presenter.Collected += (id, amount) => { collectedId = id; collectedAmount = amount; };

            _presenter.Collect(Bakery);

            Assert.AreEqual(60, _ledger.Gold);
            Assert.AreEqual(Bakery, collectedId);
            Assert.AreEqual(60, collectedAmount);
            CollectionAssert.AreEqual(new[] { "sfx_economy_collect" }, _audio.PlayedSfxIds);
            Assert.AreEqual(0, _haptics.LightTriggerCount, "Collecting is frequent: no haptic (GDD §3).");
        }

        [Test]
        public void Collect_AnEmptyStorage_IsSilent()
        {
            bool collected = false;
            _presenter.Collected += (_, __) => collected = true;

            _presenter.Collect(Bakery);

            Assert.IsFalse(collected);
            CollectionAssert.IsEmpty(_audio.PlayedSfxIds);
        }

        // ── Buying for gold ───────────────────────────────────────────────────────

        [TestCase(BuyAmount.One, 1)]
        [TestCase(BuyAmount.Ten, 10)]
        public void Buy_WhenTheGoldCovers_BuysOnTheTap_WithOneSoundAndOneHaptic(BuyAmount amount, int count)
        {
            _ledger.AddGold(5_000);
            long quoted = _manager.PriceOf(Bakery, count);
            string bought = null;
            _presenter.BusinessBought += id => bought = id;

            _presenter.Buy(Bakery, amount).Forget();

            Assert.AreEqual(count, _manager.UnitsOwned(Bakery), "No confirmation for a gold purchase (GDD §8).");
            Assert.AreEqual(5_000 - quoted, _ledger.Gold, "The price shown is the price taken.");
            CollectionAssert.AreEqual(new[] { "sfx_economy_buy" }, _audio.PlayedSfxIds);
            Assert.AreEqual(1, _haptics.LightTriggerCount);
            Assert.AreEqual(Bakery, bought);
        }

        [Test]
        public void Buy_Max_BuysAsManyAsTheGoldPaysFor()
        {
            _ledger.AddGold(1_050);

            _presenter.Buy(Bakery, BuyAmount.Max).Forget();

            Assert.AreEqual(10, _manager.UnitsOwned(Bakery));
            Assert.AreEqual(50, _ledger.Gold);
            Assert.AreEqual(1, _haptics.LightTriggerCount, "One purchase, one haptic — however many units.");
        }

        [Test]
        public void Buy_PastTheUnitsCeiling_DoesNothing()
        {
            Buy(Bakery, EconomyConfig.MaxUnitsPerType);
            _audio.PlayedSfxIds.Clear();
            long gold = _ledger.Gold;

            _presenter.Buy(Bakery, BuyAmount.One).Forget();
            _presenter.Buy(Bakery, BuyAmount.Max).Forget();

            Assert.AreEqual(gold, _ledger.Gold);
            CollectionAssert.IsEmpty(_audio.PlayedSfxIds);
            Assert.AreEqual(0, _haptics.LightTriggerCount);
        }

        // ── Passive sound ─────────────────────────────────────────────────────────

        [Test]
        public void AStorageFilling_PlaysTheFullSound_OnlyWhileTheScreenIsVisible()
        {
            Buy(Bakery, 1);
            _presenter.SetScreenVisible(false);
            Elapse(TimeSpan.FromHours(24));
            CollectionAssert.IsEmpty(_audio.PlayedSfxIds, "A hidden screen stays silent (GDD §13).");

            _presenter.Collect(Bakery);
            _audio.PlayedSfxIds.Clear();
            _presenter.SetScreenVisible(true);
            Elapse(TimeSpan.FromHours(24));

            CollectionAssert.AreEqual(new[] { "sfx_economy_business_full" }, _audio.PlayedSfxIds);
            Assert.AreEqual(0, _haptics.LightTriggerCount, "A passive moment: no haptic.");
        }

        [Test]
        public void ManyStoragesFillingTogether_PlayOneSound()
        {
            Buy(Bakery, 1);
            Buy(Tavern, 1);
            _presenter.SetScreenVisible(true);

            Elapse(TimeSpan.FromHours(30));   // both filled while the app was away

            CollectionAssert.AreEqual(new[] { "sfx_economy_business_full" }, _audio.PlayedSfxIds);
        }

        [Test]
        public void BecomingVisible_ReRendersTheText()
        {
            int changes = 0;
            _presenter.StateChanged += () => changes++;

            _presenter.SetScreenVisible(true);
            _presenter.SetScreenVisible(true);

            Assert.AreEqual(1, changes, "Once per change of visibility.");
        }

        [Test]
        public void Dispose_StopsListening()
        {
            Buy(Bakery, 1);
            _presenter.SetScreenVisible(true);
            int changes = 0;
            _presenter.StateChanged += () => changes++;

            _presenter.Dispose();
            _localization.RaiseLocaleChanged();
            Elapse(TimeSpan.FromHours(24));

            Assert.AreEqual(0, changes);
            CollectionAssert.IsEmpty(_audio.PlayedSfxIds);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static string Text(string key) => EconomyUIText.StringTable + ":" + key;

        private void UseEnglishSuffixes()
        {
            string[] formats = { "{0}K", "{0}M", "{0}B", "{0}T" };
            for (int i = 0; i < NumberSuffixes.AllKeys.Length; i++)
                _localization.Overrides[NumberSuffixes.StringTable + ":" + NumberSuffixes.AllKeys[i]] = formats[i];
        }

        private void Elapse(TimeSpan duration)
        {
            _clock.Advance(duration);
            _clock.Tick();
        }

        /// <summary>Buy through the manager, paying for it, so a test starts from owned units without the presenter's feel hooks.</summary>
        private void Buy(string businessId, int count)
        {
            _ledger.AddGold(_manager.PriceOf(businessId, count));
            Assert.IsTrue(_manager.TryBuy(businessId, count));
        }

        private BusinessDefinition Business(string id, long price, long perHour)
        {
            var business = ScriptableObject.CreateInstance<BusinessDefinition>();
            business.BusinessId  = id;
            business.UnitPrice   = price;
            business.GoldPerHour = perHour;
            _created.Add(business);
            return business;
        }

        private EconomyConfig Config(params BusinessDefinition[] businesses)
        {
            var config = ScriptableObject.CreateInstance<EconomyConfig>();
            config.Businesses = businesses;
            _created.Add(config);
            return config;
        }
    }
}
