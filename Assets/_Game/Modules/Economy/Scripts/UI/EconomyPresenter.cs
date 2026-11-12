using System;
using System.Collections.Generic;
using System.Globalization;
using Cysharp.Threading.Tasks;
using KingdomRuler.Shared.Text;
using KingdomRuler.Systems.Audio;
using KingdomRuler.Systems.Haptics;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Systems.Localization;
using KingdomRuler.Systems.Popups;
using KingdomRuler.Systems.Popups.Confirm;

namespace KingdomRuler.Modules.Economy.UI
{
    /// <summary>
    /// Mediates between <see cref="EconomyManager"/> and the Economy screen: turns model state into
    /// localized, abbreviated view data, carries collects and purchases to the manager, runs the
    /// crystal top-up confirmation, and owns the feel hooks (sound, haptics). Plain C#, no Unity UI types.
    /// </summary>
    /// <remarks>
    /// <para><b>Gold purchases are not confirmed; crystal purchases always are</b> (GDD §8). A buy the
    /// gold covers happens on the tap. One it does not opens a confirmation offering to cover exactly
    /// the missing gold with crystals; the manager re-reads every price when it charges.</para>
    ///
    /// <para><b>Polled, not pushed.</b> Storages fill continuously and publish nothing, so the rows,
    /// the gold total and an open panel re-read their numbers. The one thing pushed is the manager's
    /// <see cref="EconomyManager.StoragesFilled"/>, which plays the "business full" sound — only while
    /// the screen is visible, and once per tick however many filled.</para>
    ///
    /// <para><b>One haptic per action.</b> A purchase triggers one; a collect triggers none — it is
    /// frequent, and GDD §3 keeps haptics for purchases, swipes and level-ups.</para>
    /// </remarks>
    public sealed class EconomyPresenter : IDisposable
    {
        private static class Sfx
        {
            public const string Collect         = "sfx_economy_collect";
            public const string Buy             = "sfx_economy_buy";
            public const string BuyWithCrystals = "sfx_economy_buy_crystals";
            public const string Refused         = "sfx_economy_refused";
            public const string BusinessFull    = "sfx_economy_business_full";
        }

        private readonly EconomyManager       _manager;
        private readonly KingdomLedger        _ledger;
        private readonly IAudioService        _audio;
        private readonly IHapticService       _haptics;
        private readonly ILocalizationService _localization;
        private readonly PopupSystem          _popups;

        private readonly Dictionary<string, BusinessDefinition> _businessById = new();
        private readonly string[] _businessIds;

        /// <summary>Resolved on first use and dropped on a language change, so formatting a number resolves nothing.</summary>
        private NumberSuffixes? _suffixes;
        private bool _isScreenVisible;

        /// <summary>The language changed, or the screen came back into view: re-resolve every text.</summary>
        public event Action StateChanged;

        /// <summary>A business was collected from: its id and the gold moved.</summary>
        public event Action<string, long> Collected;

        /// <summary>Units of a business were bought, for gold or with a crystal top-up.</summary>
        public event Action<string> BusinessBought;

        public EconomyPresenter(
            EconomyManager       manager,
            KingdomLedger        ledger,
            IAudioService        audio,
            IHapticService       haptics,
            ILocalizationService localization,
            PopupSystem          popups)
        {
            _manager      = manager      ?? throw new ArgumentNullException(nameof(manager));
            _ledger       = ledger       ?? throw new ArgumentNullException(nameof(ledger));
            _audio        = audio        ?? throw new ArgumentNullException(nameof(audio));
            _haptics      = haptics      ?? throw new ArgumentNullException(nameof(haptics));
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));

            // A UnityEngine.Object: `??` bypasses Unity's null check and would let a destroyed object through.
            if (popups == null) throw new ArgumentNullException(nameof(popups));
            _popups = popups;

            _businessIds = new string[_manager.Businesses.Count];
            for (int i = 0; i < _businessIds.Length; i++)
            {
                var business = _manager.Businesses[i];
                _businessIds[i] = business.BusinessId;
                _businessById[business.BusinessId] = business;
            }

            _manager.StoragesFilled     += HandleStoragesFilled;
            _localization.LocaleChanged += HandleLocaleChanged;
        }

        public void Dispose()
        {
            _manager.StoragesFilled     -= HandleStoragesFilled;
            _localization.LocaleChanged -= HandleLocaleChanged;
        }

        // ── Polled by the view ────────────────────────────────────────────────────

        /// <summary>Every business, in display order: the rows the screen must have, one each.</summary>
        public IReadOnlyList<string> BusinessIds => _businessIds;

        public BusinessRowViewData GetRow(string businessId) =>
            new BusinessRowViewData(ArtworkOf(businessId), _manager.UnitsOwned(businessId), _manager.Stored(businessId),
                                    _manager.StorageCapacity(businessId), _manager.IsStorageFull(businessId));

        /// <summary>The balance, for the screen's gold total.</summary>
        public long Gold => _ledger.Gold;

        // ── Text, read on StateChanged or when the number in it changes ───────────

        /// <summary>
        /// An amount as this game writes it: in full below 10,000, then "12.3K" (<see cref="NumberFormat"/>).
        /// Views ask again only when <see cref="NumberFormat.DisplayedValue"/> changes.
        /// </summary>
        public string Amount(long value) => NumberFormat.Abbreviate(value, Suffixes, CultureInfo.CurrentCulture);

        public string BusinessName(string businessId) =>
            _businessById.TryGetValue(businessId ?? string.Empty, out var business)
                ? _localization.Resolve(BusinessDefinition.StringTable, business.NameKey)
                : string.Empty;

        /// <summary>"12 owned".</summary>
        public string UnitsOwnedText(int unitsOwned) => Resolve(EconomyUIText.UnitsOwned, Amount(unitsOwned));

        // ── The panel's data, re-read by the open panel ───────────────────────────

        public BusinessPopupData GetBusiness(string businessId)
        {
            _businessById.TryGetValue(businessId ?? string.Empty, out var business);

            int  room     = _manager.RoomFor(businessId);
            bool maxed    = room == 0;
            int  maxCount = CountFor(businessId, BuyAmount.Max);
            long stored   = _manager.Stored(businessId);
            long capacity = _manager.StorageCapacity(businessId);

            return new BusinessPopupData(
                business != null ? business.Artwork : null,
                BusinessName(businessId),
                business != null ? _localization.Resolve(BusinessDefinition.StringTable, business.FlavorKey) : string.Empty,
                UnitsOwnedText(_manager.UnitsOwned(businessId)),
                Resolve(EconomyUIText.Rate, Amount(_manager.GoldPerHour(businessId))),
                stored, capacity, Amount(stored), Amount(capacity),
                canCollect: stored > 0,
                canBuyOne: room >= 1, canBuyTen: room >= 10, canBuyMax: room >= 1,
                affordsOne: _manager.CanAfford(businessId, 1),
                affordsTen: _manager.CanAfford(businessId, 10),
                affordsMax: _manager.MaxAffordable(businessId) > 0,
                onePrice: maxed ? null : Amount(_manager.PriceOf(businessId, 1)),
                tenPrice: maxed ? null : Amount(_manager.PriceOf(businessId, 10)),
                maxText:  maxed ? null : Resolve(EconomyUIText.BuyMax, maxCount),
                maxPrice: maxed ? null : Amount(_manager.PriceOf(businessId, maxCount)),
                maxedText: maxed ? Resolve(EconomyUIText.Maxed) : null);
        }

        // ── Player actions ────────────────────────────────────────────────────────

        /// <summary>Empty one business's storage into the balance — from its row or from its panel, the same either way.</summary>
        public void Collect(string businessId)
        {
            long collected = _manager.Collect(businessId);
            if (collected <= 0) return;

            _audio.PlaySfx(Sfx.Collect);
            Collected?.Invoke(businessId, collected);
        }

        /// <summary>
        /// A buy button. When the gold covers it, the units are bought at once, unconfirmed. When it does
        /// not, a confirmation offers to cover exactly the missing gold with crystals; Max with nothing
        /// affordable offers one unit (GDD §8).
        /// </summary>
        /// <remarks>A <c>UniTask</c>, so the panel's choice loop can await the confirmation before reading its next press.</remarks>
        public async UniTask Buy(string businessId, BuyAmount amount)
        {
            int count = CountFor(businessId, amount);

            // Past the units ceiling the button is inactive; a tap that slipped through does nothing.
            if (count <= 0 || count > _manager.RoomFor(businessId)) return;

            if (_manager.CanAfford(businessId, count))
            {
                if (_manager.TryBuy(businessId, count)) Bought(businessId, Sfx.Buy);
                else _audio.PlaySfx(Sfx.Refused);
                return;
            }

            var choice = await _popups.Create<ConfirmPopup>().Ask(
                () => new ConfirmPopupData(Resolve(EconomyUIText.TopUpTitle), DescribeTopUp(businessId, count),
                                           canConfirm: _manager.CanAffordCrystalTopUp(businessId, count)),
                // The shortfall vanished (a collect elsewhere) or the room did: the question no longer applies.
                closeWhen: () => _manager.GoldShortfall(businessId, count) == 0 || _manager.RoomFor(businessId) < count);

            if (choice != ConfirmPopupChoice.Confirm) return;

            // The manager re-reads the price and the crystals now, not what the popup showed.
            if (_manager.TryBuyWithCrystalTopUp(businessId, count)) Bought(businessId, Sfx.BuyWithCrystals);
            else _audio.PlaySfx(Sfx.Refused);
        }

        /// <summary>Only sound depends on this: a storage filling on a hidden screen is silent.</summary>
        public void SetScreenVisible(bool visible)
        {
            if (_isScreenVisible == visible) return;
            _isScreenVisible = visible;

            if (visible) StateChanged?.Invoke();
        }

        // ── Private ───────────────────────────────────────────────────────────────

        /// <summary>How many units a button buys right now. Max is as many as affordable, or one — offered with crystals — when none is.</summary>
        private int CountFor(string businessId, BuyAmount amount) => amount switch
        {
            BuyAmount.One => 1,
            BuyAmount.Ten => 10,
            _             => Math.Max(1, _manager.MaxAffordable(businessId))
        };

        private void Bought(string businessId, string sfx)
        {
            _audio.PlaySfx(sfx);
            _haptics.TriggerLight();
            BusinessBought?.Invoke(businessId);
        }

        private string DescribeTopUp(string businessId, int count)
        {
            string body = Resolve(EconomyUIText.TopUpBody,
                                  Amount(_manager.GoldShortfall(businessId, count)), count, BusinessName(businessId),
                                  Amount(_manager.CrystalTopUpCost(businessId, count)));
            return _manager.CanAffordCrystalTopUp(businessId, count)
                ? body
                : body + "\n" + Resolve(EconomyUIText.NotEnoughCrystals);
        }

        private UnityEngine.Sprite ArtworkOf(string businessId) =>
            _businessById.TryGetValue(businessId ?? string.Empty, out var business) ? business.Artwork : null;

        private NumberSuffixes Suffixes
        {
            get
            {
                if (_suffixes == null)
                {
                    var keys = NumberSuffixes.AllKeys;
                    _suffixes = new NumberSuffixes(ResolveShared(keys[0]), ResolveShared(keys[1]),
                                                   ResolveShared(keys[2]), ResolveShared(keys[3]));
                }
                return _suffixes.Value;
            }
        }

        private string ResolveShared(string key) => _localization.Resolve(NumberSuffixes.StringTable, key);

        private void HandleStoragesFilled()
        {
            if (_isScreenVisible) _audio.PlaySfx(Sfx.BusinessFull);
        }

        private void HandleLocaleChanged()
        {
            _suffixes = null;
            StateChanged?.Invoke();
        }

        private string Resolve(string key, params object[] args) =>
            args.Length == 0
                ? _localization.Resolve(EconomyUIText.StringTable, key)
                : _localization.Resolve(EconomyUIText.StringTable, key, args);
    }
}
