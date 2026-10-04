using System;
using System.Collections.Generic;
using KingdomRuler.Core;
using KingdomRuler.Shared.Ledger;
using KingdomRuler.Shared.Services;
using KingdomRuler.Modules.Trade.Domain;

namespace KingdomRuler.Modules.Trade.Presenters
{
    /// <summary>
    /// Mediates between <see cref="TradeManager"/> (Model) and the Trade Views.
    /// Plain C# — no MonoBehaviour, no DOTween, fully unit-testable.
    /// </summary>
    /// <remarks>
    /// <para>Everything a View draws arrives as a display struct with strings already localized
    /// and sprites already looked up, so no View ever touches a <c>TradeOffer</c>, the
    /// <c>TradeResourceRegistry</c>, or <c>ILocalizationService</c>.</para>
    ///
    /// <para><b>Prices are read from the Manager, never recomputed.</b> Three of them here
    /// (instant refresh, crystal upgrade, paired upgrade) and each is a chance to repeat the bug
    /// <c>docs/modules/Laws.md</c> §6.3 records: a Presenter that duplicates a pricing formula
    /// agrees with the Manager right up until one of them changes.</para>
    ///
    /// <para><b>Warehouse fill is not pushed from here.</b> Regeneration is continuous, so the
    /// View polls it each frame with a dirty-check rather than the Ledger publishing ~24 events a
    /// second (see <c>KingdomLedger.AccruePassiveResourceRegen</c>). Discrete changes — accepting
    /// an offer, an upgrade, a Cities purchase — still arrive on the bus as
    /// <see cref="ResourceChanged"/>.</para>
    /// </remarks>
    public sealed class TradePresenter : IDisposable
    {
        private static class SfxIds
        {
            public const string OfferAccept      = "sfx_trade_offer_accept";
            public const string OfferRefused     = "sfx_trade_offer_refused";
            public const string OffersRefreshed  = "sfx_trade_offers_refreshed";
            public const string WarehouseUpgrade = "sfx_trade_warehouse_upgrade";
            public const string CrystalSpend     = "sfx_trade_crystal_spend";
        }

        private readonly TradeManager   _manager;
        private readonly KingdomLedger  _ledger;
        private readonly EventBus       _eventBus;
        private readonly IAudioService  _audio;
        private readonly IHapticService _haptics;
        private readonly ILocalizationService  _localization;
        private readonly TradeResourceRegistry _resources;

        /// <summary>Whether the Trade screen is on-screen. Gates audio only.</summary>
        private bool _isScreenVisible;

        /// <summary>Re-render everything.</summary>
        public event Action OnStateChanged;

        /// <summary>A warehouse was upgraded — the View plays that tile's celebration.</summary>
        public event Action<TradeResourceType> OnWarehouseUpgraded;

        public TradePresenter(
            TradeManager   manager,
            KingdomLedger  ledger,
            EventBus       eventBus,
            IAudioService  audio,
            IHapticService haptics,
            ILocalizationService  localization,
            TradeResourceRegistry resources)
        {
            _manager      = manager      ?? throw new ArgumentNullException(nameof(manager));
            _ledger       = ledger       ?? throw new ArgumentNullException(nameof(ledger));
            _eventBus     = eventBus     ?? throw new ArgumentNullException(nameof(eventBus));
            _audio        = audio        ?? throw new ArgumentNullException(nameof(audio));
            _haptics      = haptics      ?? throw new ArgumentNullException(nameof(haptics));
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));

            // Explicit == null rather than ??, because TradeResourceRegistry is a
            // UnityEngine.Object: ?? bypasses Unity's overloaded equality, so a destroyed asset
            // would pass this check and then fail on first use.
            if (resources == null) throw new ArgumentNullException(nameof(resources));
            _resources = resources;

            _manager.TradeStateChanged += NotifyStateChanged;
            // Cross-module: a Cities purchase or an occurrence moves a trade resource and the
            // warehouse tiles must follow. Deliberately does NOT fire for passive regen.
            _eventBus.Subscribe<ResourceChanged>(HandleResourceChanged);
            // Every string on a display struct is a resolved copy that nothing else updates.
            _localization.LocaleChanged += NotifyStateChanged;
        }

        public void Dispose()
        {
            _manager.TradeStateChanged -= NotifyStateChanged;
            _eventBus.Unsubscribe<ResourceChanged>(HandleResourceChanged);
            _localization.LocaleChanged -= NotifyStateChanged;
        }

        // ── Read-only state ───────────────────────────────────────────────────────

        public int   OfferCount          => _manager.ActiveOffers.Count;
        public bool  HasOffers           => OfferCount > 0;
        public float SecondsUntilRefresh => _manager.GetSecondsUntilRefresh();

        /// <summary>Read from the Manager, so the price shown is by construction the price charged.</summary>
        public int  InstantRefreshCost       => _manager.InstantRefreshCost;
        public bool CanAffordInstantRefresh  => _ledger.Crystals >= InstantRefreshCost;

        // ── Display data ──────────────────────────────────────────────────────────

        /// <summary>One display row per live offer, in list order.</summary>
        public List<TradeOfferDisplayData> GetOfferDisplays()
        {
            var displays = new List<TradeOfferDisplayData>(_manager.ActiveOffers.Count);

            foreach (var offer in _manager.ActiveOffers)
            {
                var evaluation = _manager.Evaluate(offer.Id);
                displays.Add(new TradeOfferDisplayData(
                    offer.Id,
                    BuildLines(offer.GiveResources),
                    BuildLines(offer.ReceiveResources),
                    offer.TotalGive,
                    offer.TotalReceive,
                    evaluation.CanAccept,
                    evaluation.HasOverflowWarning));
            }

            return displays;
        }

        /// <summary>
        /// Build the confirmation panel for an offer. Returns false only when the offer is gone.
        /// </summary>
        /// <remarks>
        /// An unaffordable offer still returns <c>true</c>, with the reasons in
        /// <see cref="TradeConfirmDisplayData.BlockerMessages"/> — GDD §7 requires the panel to
        /// explain rather than the row to be disabled.
        /// </remarks>
        public bool TryGetConfirmation(string offerId, out TradeConfirmDisplayData display)
        {
            TradeOffer offer = null;
            foreach (var candidate in _manager.ActiveOffers)
                if (candidate.Id == offerId) { offer = candidate; break; }

            if (offer == null) { display = default; return false; }

            var evaluation = _manager.Evaluate(offerId);
            var blockers = new List<string>();
            var warnings = new List<string>();

            foreach (var issue in evaluation.Issues)
            {
                if (issue.BlocksAcceptance) blockers.Add(DescribeIssue(issue));
                else                        warnings.Add(DescribeIssue(issue));
            }

            display = new TradeConfirmDisplayData(
                offer.Id,
                BuildLines(offer.GiveResources),
                BuildLines(offer.ReceiveResources),
                evaluation.CanAccept,
                blockers,
                warnings);
            return true;
        }

        /// <summary>One warehouse tile's state and both upgrade prices.</summary>
        public WarehouseDisplayData GetWarehouseDisplay(TradeResourceType type)
        {
            var state  = _ledger.GetTradeResource(type);
            var paired = _manager.GetPairedResource(type);

            float fill = state.Capacity > 0f
                ? Math.Min(1f, Math.Max(0f, state.Amount / state.Capacity))
                : 0f;

            bool canUpgrade   = _manager.CanUpgradeFurther(type);
            int  crystalCost  = _manager.GetWarehouseCrystalCost(type);
            float pairedCost  = _manager.GetWarehousePairedCost(type);
            string pairedName = ResolveResourceName(paired);

            return new WarehouseDisplayData(
                type, ResolveResourceName(type), _resources.IconFor(type),
                state.Amount, state.Capacity, fill,
                Resolve(TradeUIText.WarehouseAmount, Math.Floor(state.Amount), state.Capacity),
                _manager.GetWarehouseLevel(type), canUpgrade,
                crystalCost,
                canUpgrade && _ledger.Crystals >= crystalCost,
                canUpgrade ? Resolve(TradeUIText.UpgradeCostCrystals, crystalCost)
                           : Resolve(TradeUIText.UpgradeMaxed),
                paired, pairedName,
                pairedCost,
                canUpgrade && _ledger.GetTradeResource(paired).Amount >= pairedCost,
                canUpgrade ? Resolve(TradeUIText.UpgradeCostPaired, Math.Ceiling(pairedCost), pairedName)
                           : Resolve(TradeUIText.UpgradeMaxed));
        }

        /// <summary>The refresh countdown, already formatted and localized.</summary>
        public string GetRefreshCountdownText()
        {
            int total   = (int)Math.Ceiling(SecondsUntilRefresh);
            int minutes = total / 60;
            int seconds = total % 60;
            return Resolve(TradeUIText.RefreshIn, $"{minutes}:{seconds:D2}");
        }

        // ── User intent — logic first, then feedback ──────────────────────────────

        /// <summary>
        /// Player confirmed a trade. Returns whether it went through.
        /// </summary>
        /// <remarks>
        /// A full warehouse does not refuse the trade; the player already saw the warning on the
        /// panel and chose to forfeit the excess. Only an unpayable give side refuses.
        /// </remarks>
        public bool OnOfferAcceptRequested(string offerId)
        {
            var result = _manager.AcceptOffer(offerId);

            if (!result.Accepted)
            {
                _audio.PlaySfx(SfxIds.OfferRefused);
                NotifyStateChanged();     // the panel re-renders with the reason
                return false;
            }

            _audio.PlaySfx(SfxIds.OfferAccept);
            _haptics.TriggerLight();
            return true;                  // TradeStateChanged already re-rendered
        }

        public bool OnInstantRefreshRequested()
        {
            if (!_manager.InstantRefresh()) return false;

            _audio.PlaySfx(SfxIds.CrystalSpend);
            _audio.PlaySfx(SfxIds.OffersRefreshed);
            _haptics.TriggerLight();
            return true;
        }

        public bool OnCrystalUpgradeRequested(TradeResourceType type)
        {
            if (!_manager.UpgradeWarehouseWithCrystals(type)) return false;

            _audio.PlaySfx(SfxIds.CrystalSpend);
            _audio.PlaySfx(SfxIds.WarehouseUpgrade);
            _haptics.TriggerLight();
            OnWarehouseUpgraded?.Invoke(type);
            return true;
        }

        public bool OnPairedUpgradeRequested(TradeResourceType type)
        {
            if (!_manager.UpgradeWarehouseWithPairedResource(type)) return false;

            _audio.PlaySfx(SfxIds.WarehouseUpgrade);
            _haptics.TriggerLight();
            OnWarehouseUpgraded?.Invoke(type);
            return true;
        }

        /// <summary>
        /// Told by the View when the Trade screen is shown or hidden. Gates audio only — the
        /// offer timer and regen keep running on other tabs, so the screen is correct on return.
        /// </summary>
        public void SetScreenVisible(bool visible)
        {
            if (_isScreenVisible == visible) return;
            _isScreenVisible = visible;

            if (visible) NotifyStateChanged();
        }

        // ── Private ───────────────────────────────────────────────────────────────

        private List<TradeResourceLineData> BuildLines(
            IReadOnlyDictionary<TradeResourceType, float> amounts)
        {
            var lines = new List<TradeResourceLineData>(amounts.Count);

            foreach (var pair in amounts)
                lines.Add(new TradeResourceLineData(
                    pair.Key,
                    ResolveResourceName(pair.Key),
                    _resources.IconFor(pair.Key),
                    pair.Value,
                    pair.Value.ToString("0")));

            return lines;
        }

        /// <summary>Turn a Domain issue into a sentence the panel can show.</summary>
        private string DescribeIssue(TradeIssue issue)
        {
            switch (issue.Kind)
            {
                case TradeIssueKind.InsufficientResource:
                    return Resolve(TradeUIText.BlockerInsufficient,
                                   Math.Ceiling(issue.Shortfall), ResolveResourceName(issue.Resource));

                case TradeIssueKind.WouldOverflowWarehouse:
                    return Resolve(TradeUIText.WarningOverflow,
                                   ResolveResourceName(issue.Resource), Math.Floor(issue.Available));

                default:
                    return Resolve(TradeUIText.BlockerUnavailable);
            }
        }

        /// <summary>
        /// Resource names come from the shared registry, not a Trade-owned key: Cities lists them
        /// on purchase costs and Random Occurrences in outcome text (<c>ARCHITECTURE.md</c> §4.4).
        /// </summary>
        private string ResolveResourceName(TradeResourceType type) =>
            _localization.Resolve(TradeResourceDefinition.StringTable, _resources.NameKeyFor(type));

        private string Resolve(string key, params object[] args) =>
            args == null || args.Length == 0
                ? _localization.Resolve(TradeUIText.StringTable, key)
                : _localization.Resolve(TradeUIText.StringTable, key, args);

        private void HandleResourceChanged(ResourceChanged _) => NotifyStateChanged();

        private void NotifyStateChanged() => OnStateChanged?.Invoke();
    }
}
