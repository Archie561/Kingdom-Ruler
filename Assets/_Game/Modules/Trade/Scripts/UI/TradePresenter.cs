using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using KingdomRuler.Shared.Text;
using KingdomRuler.Systems.Audio;
using KingdomRuler.Systems.Haptics;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Systems.Localization;
using KingdomRuler.Systems.Popups;
using KingdomRuler.Systems.Popups.Confirm;

namespace KingdomRuler.Modules.Trade.UI
{
    /// <summary>
    /// Mediates between <see cref="TradeManager"/> and <see cref="TradeScreenView"/>: turns model
    /// state into localized view data, runs the confirmation flows, and owns the feel hooks (sound,
    /// haptics). Plain C#, no Unity UI types.
    /// </summary>
    /// <remarks>
    /// <para><b>Polled, not pushed.</b> It listens to nothing on the event bus. Stock changes
    /// continuously with regeneration and from other mechanics, so the tiles and any open popup
    /// re-read it; only the offer list, which changes in discrete steps, is pushed
    /// (<see cref="TradeManager.OffersChanged"/>). One accept is therefore exactly one re-render.</para>
    ///
    /// <para><b>Every price comes from the manager</b>, and every purchase is confirmed: the
    /// confirmation re-reads the price while open, and the manager re-reads it again when charging.</para>
    /// </remarks>
    public sealed class TradePresenter : IDisposable
    {
        private static class Sfx
        {
            public const string OfferAccept      = "sfx_trade_offer_accept";
            public const string OfferRefused     = "sfx_trade_offer_refused";
            public const string OffersRefreshed  = "sfx_trade_offers_refreshed";
            public const string WarehouseUpgrade = "sfx_trade_warehouse_upgrade";
        }

        /// <summary>A unit that lands faster than this would make the next-unit bar flicker, so it is hidden.</summary>
        private const float MinSecondsPerUnitForBar = 1f;

        private readonly TradeManager          _manager;
        private readonly KingdomLedger         _ledger;
        private readonly IAudioService         _audio;
        private readonly IHapticService        _haptics;
        private readonly ILocalizationService  _localization;
        private readonly TradeResourceRegistry _resources;
        private readonly PopupSystem           _popups;

        /// <summary>The refresh deadline last announced, so a refresh can be told from an accept: only a refresh moves it.</summary>
        private DateTime? _shownDeadline;
        private bool      _isScreenVisible;

        /// <summary>The offer list or the language changed: re-render the list and the text.</summary>
        public event Action StateChanged;

        /// <summary>A new batch of offers is on screen, after its <see cref="StateChanged"/>.</summary>
        public event Action OffersArrived;

        /// <summary>An offer was accepted: what was received and what was lost.</summary>
        public event Action<TradeReceiptViewData> TradeCompleted;

        public event Action<TradeResourceType> WarehouseUpgraded;

        public TradePresenter(
            TradeManager          manager,
            KingdomLedger         ledger,
            IAudioService         audio,
            IHapticService        haptics,
            ILocalizationService  localization,
            TradeResourceRegistry resources,
            PopupSystem           popups)
        {
            _manager      = manager      ?? throw new ArgumentNullException(nameof(manager));
            _ledger       = ledger       ?? throw new ArgumentNullException(nameof(ledger));
            _audio        = audio        ?? throw new ArgumentNullException(nameof(audio));
            _haptics      = haptics      ?? throw new ArgumentNullException(nameof(haptics));
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));

            // UnityEngine.Objects: `??` bypasses Unity's null check and would let a destroyed object through.
            if (resources == null) throw new ArgumentNullException(nameof(resources));
            if (popups == null)    throw new ArgumentNullException(nameof(popups));
            _resources = resources;
            _popups    = popups;

            _shownDeadline = _manager.NextRefreshDueUtc;

            _manager.OffersChanged      += HandleOffersChanged;
            _localization.LocaleChanged += NotifyStateChanged;
        }

        public void Dispose()
        {
            _manager.OffersChanged      -= HandleOffersChanged;
            _localization.LocaleChanged -= NotifyStateChanged;
        }

        // ── Polled by the view ────────────────────────────────────────────────────

        public ResourceTileViewData GetTile(TradeResourceType type)
        {
            var state = _ledger.GetTradeResource(type);
            return new ResourceTileViewData(type, _resources.IconFor(type), _manager.WarehouseLevel(type) + 1,
                                            state.Amount, state.Capacity);
        }

        public float SecondsUntilRefresh => _manager.SecondsUntilRefresh;

        // ── Text, read on StateChanged or when the number in it changes ───────────

        public string ResourceName(TradeResourceType type) =>
            _localization.Resolve(TradeResourceDefinition.StringTable, _resources.NameKeyFor(type));

        public string LevelText(int displayedLevel) => Resolve(TradeUIText.WarehouseLevel, displayedLevel);

        public string RefreshCountdownText(int seconds) => Resolve(TradeUIText.RefreshIn, TimeFormat.MinutesSeconds(seconds));

        public int InstantRefreshCost => _manager.InstantRefreshCost;

        /// <summary>One row per offer, in list order.</summary>
        public OfferRowViewData[] GetOffers()
        {
            var offers = _manager.Offers;
            var rows   = new OfferRowViewData[offers.Count];
            for (int i = 0; i < rows.Length; i++)
                rows[i] = new OfferRowViewData(offers[i].Id, Amounts(offers[i].Give), Amounts(offers[i].Receive));
            return rows;
        }

        // ── Popup data, re-read by the open popup ─────────────────────────────────

        /// <summary>The confirmation for an offer. An offer that is gone gives data that cannot be accepted, until <c>closeWhen</c> closes the popup.</summary>
        public TradeOfferPopupData GetOfferPopup(string offerId)
        {
            if (!_manager.TryGetOffer(offerId, out var offer))
                return new TradeOfferPopupData(new ResourceAmountViewData[0], new ResourceAmountViewData[0], null, null, false);

            var evaluation = _manager.Evaluate(offerId);
            StringBuilder blockers = null, warnings = null;

            foreach (var issue in evaluation.Issues)
            {
                if (issue.Kind == TradeIssueKind.InsufficientResource)
                    AppendLine(ref blockers, Resolve(TradeUIText.BlockerInsufficient, Number(issue.Units), ResourceName(issue.Resource)));
                else if (issue.Kind == TradeIssueKind.WouldOverflowWarehouse)
                    AppendLine(ref warnings, Resolve(TradeUIText.OfferOverflow, Number(issue.Units), ResourceName(issue.Resource)));
            }

            return new TradeOfferPopupData(Amounts(offer.Give), Amounts(offer.Receive),
                                           blockers?.ToString(), warnings?.ToString(), evaluation.CanAccept);
        }

        public WarehousePopupData GetWarehouse(TradeResourceType type)
        {
            var  state      = _ledger.GetTradeResource(type);
            int  level      = _manager.WarehouseLevel(type) + 1;
            bool full       = state.Amount >= state.Capacity;
            bool canUpgrade = _manager.CanUpgradeFurther(type);

            return new WarehousePopupData(
                type, _resources.IconFor(type),
                Resolve(TradeUIText.WarehouseTitle, ResourceName(type)),
                level, LevelText(level),
                state.Amount, state.Capacity,
                showsNextUnit: !full && _manager.SecondsPerUnit(type) >= MinSecondsPerUnitForBar,
                nextUnitPercent: Mathf.FloorToInt(_manager.NextUnitProgress(type) * 100f),
                rateText: Resolve(TradeUIText.WarehouseRate, Number(_manager.UnitsPerHour(type))),
                fullText: full
                    ? Resolve(TradeUIText.WarehouseFull)
                    : Resolve(TradeUIText.WarehouseFullIn, TimeFormat.HoursMinutesSeconds(Mathf.CeilToInt(_manager.SecondsUntilFull(type)))),
                canUpgrade: canUpgrade,
                crystalPrice: canUpgrade ? Resolve(TradeUIText.UpgradeCostCrystals, Number(_manager.CrystalUpgradeCost(type))) : null,
                pairedPrice: canUpgrade
                    ? Resolve(TradeUIText.UpgradeCostPaired, Number(_manager.PairedUpgradeCost(type)), ResourceName(TradeResourcePair.PairedWith(type)))
                    : null,
                maxedText: canUpgrade ? null : Resolve(TradeUIText.UpgradeMaxed));
        }

        // ── Player actions ────────────────────────────────────────────────────────

        /// <summary>Every tap on an offer opens its confirmation; it is accepted only if the player confirms.</summary>
        public async UniTaskVoid OpenOffer(string offerId)
        {
            var choice = await _popups.Create<TradeOfferPopup>().Ask(
                () => GetOfferPopup(offerId),
                closeWhen: () => !_manager.TryGetOffer(offerId, out _));   // refreshed away while open

            if (choice != TradeOfferPopupChoice.Accept) return;

            // The manager re-evaluates now, not from what the popup showed.
            var result = _manager.AcceptOffer(offerId);
            if (!result.Accepted)
            {
                _audio.PlaySfx(Sfx.OfferRefused);
                return;
            }

            _audio.PlaySfx(Sfx.OfferAccept);
            _haptics.TriggerLight();
            TradeCompleted?.Invoke(DescribeReceipt(result));
        }

        public async UniTaskVoid RequestInstantRefresh()
        {
            var deadlineWhenOpened = _manager.NextRefreshDueUtc;

            var choice = await _popups.Create<ConfirmPopup>().Ask(
                () => new ConfirmPopupData(Resolve(TradeUIText.RefreshTitle), DescribeRefresh(),
                                           canConfirm: _manager.CanAffordInstantRefresh),
                closeWhen: () => _manager.NextRefreshDueUtc != deadlineWhenOpened);   // the free refresh landed

            if (choice != ConfirmPopupChoice.Confirm || !_manager.TryInstantRefresh()) return;

            // The refresh sound is played by HandleOffersChanged, like any refresh; this adds the purchase's haptic.
            _haptics.TriggerLight();
        }

        /// <summary>
        /// Open one resource's warehouse popup and keep it open until the player taps outside it.
        /// Each upgrade asks for confirmation on top; confirming closes only the confirmation.
        /// </summary>
        public async UniTaskVoid OpenWarehouse(TradeResourceType type)
        {
            var popup = _popups.Create<WarehousePopup>().Show(() => GetWarehouse(type));

            while (popup.IsOpen)
            {
                WarehousePayment? payment = await popup.WaitForChoice();
                if (payment.HasValue) await ConfirmUpgrade(type, payment.Value);
            }
        }

        /// <summary>Only sound depends on this: a refresh on a hidden screen updates the list silently.</summary>
        public void SetScreenVisible(bool visible)
        {
            if (_isScreenVisible == visible) return;
            _isScreenVisible = visible;

            if (visible) NotifyStateChanged();
        }

        // ── Private ───────────────────────────────────────────────────────────────

        private async UniTask ConfirmUpgrade(TradeResourceType type, WarehousePayment payment)
        {
            var choice = await _popups.Create<ConfirmPopup>().Ask(
                () => new ConfirmPopupData(Resolve(TradeUIText.UpgradeTitle), DescribeUpgrade(type, payment),
                                           canConfirm: CanAffordUpgrade(type, payment)),
                closeWhen: () => !_manager.CanUpgradeFurther(type));

            if (choice != ConfirmPopupChoice.Confirm) return;

            // The manager re-reads the price now, not from what the popup showed.
            bool upgraded = payment == WarehousePayment.Crystals
                ? _manager.TryUpgradeWithCrystals(type)
                : _manager.TryUpgradeWithPairedResource(type);
            if (!upgraded) return;

            _audio.PlaySfx(Sfx.WarehouseUpgrade);
            _haptics.TriggerLight();
            WarehouseUpgraded?.Invoke(type);
        }

        private bool CanAffordUpgrade(TradeResourceType type, WarehousePayment payment) =>
            payment == WarehousePayment.Crystals
                ? _manager.CanAffordCrystalUpgrade(type)
                : _manager.CanAffordPairedUpgrade(type);

        private string DescribeUpgrade(TradeResourceType type, WarehousePayment payment)
        {
            int    nextLevel = _manager.WarehouseLevel(type) + 2;   // displayed from 1, one level up
            string capacity  = Number(_manager.CapacityAfterUpgrade(type));
            string name      = ResourceName(type);

            if (payment == WarehousePayment.Crystals)
            {
                string offer = Resolve(TradeUIText.UpgradeBodyCrystals, Number(_manager.CrystalUpgradeCost(type)), name, nextLevel, capacity);
                return _manager.CanAffordCrystalUpgrade(type) ? offer : offer + "\n" + Resolve(TradeUIText.NotEnoughCrystals);
            }

            var    paired     = TradeResourcePair.PairedWith(type);
            string pairedName = ResourceName(paired);
            string body = Resolve(TradeUIText.UpgradeBodyPaired, Number(_manager.PairedUpgradeCost(type)), pairedName, name, nextLevel, capacity);
            return _manager.CanAffordPairedUpgrade(type)
                ? body
                : body + "\n" + Resolve(TradeUIText.NotEnoughPaired, pairedName, Number(_ledger.GetTradeResource(paired).Amount));
        }

        private string DescribeRefresh()
        {
            string body = Resolve(TradeUIText.RefreshBody, Number(_manager.InstantRefreshCost),
                                  TimeFormat.MinutesSeconds(Mathf.CeilToInt(_manager.SecondsUntilRefresh)));
            return _manager.CanAffordInstantRefresh ? body : body + "\n" + Resolve(TradeUIText.NotEnoughCrystals);
        }

        private TradeReceiptViewData DescribeReceipt(TradeAcceptResult result)
        {
            var lines = new List<TradeReceiptLineViewData>(result.Received.Count + result.Forfeited.Count);

            foreach (var amount in result.Received)
                if (amount.Units > 0)
                    lines.Add(new TradeReceiptLineViewData(_resources.IconFor(amount.Resource),
                        Resolve(TradeUIText.ReceiptReceived, Number(amount.Units), ResourceName(amount.Resource)), isLoss: false));

            foreach (var amount in result.Forfeited)
                lines.Add(new TradeReceiptLineViewData(_resources.IconFor(amount.Resource),
                    Resolve(TradeUIText.ReceiptForfeited, Number(amount.Units), ResourceName(amount.Resource)), isLoss: true));

            return new TradeReceiptViewData(lines.ToArray());
        }

        private ResourceAmountViewData[] Amounts(IReadOnlyList<TradeAmount> amounts)
        {
            var result = new ResourceAmountViewData[amounts.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = new ResourceAmountViewData(_resources.IconFor(amounts[i].Resource), amounts[i].Units, ResourceName(amounts[i].Resource));
            return result;
        }

        private void HandleOffersChanged()
        {
            bool refreshed = _manager.NextRefreshDueUtc != _shownDeadline;
            _shownDeadline = _manager.NextRefreshDueUtc;

            if (refreshed && _isScreenVisible) _audio.PlaySfx(Sfx.OffersRefreshed);

            NotifyStateChanged();

            // After the re-render, so the view animates the rows it already shows.
            if (refreshed) OffersArrived?.Invoke();
        }

        private void NotifyStateChanged() => StateChanged?.Invoke();

        private string Resolve(string key, params object[] args) =>
            args.Length == 0
                ? _localization.Resolve(TradeUIText.StringTable, key)
                : _localization.Resolve(TradeUIText.StringTable, key, args);

        /// <summary>Whole numbers with group separators: "1,140".</summary>
        private static string Number(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

        private static void AppendLine(ref StringBuilder builder, string line)
        {
            if (builder == null) builder = new StringBuilder(line);
            else builder.Append('\n').Append(line);
        }
    }
}
