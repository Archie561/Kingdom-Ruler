using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Modules.Trade.Presenters;

namespace KingdomRuler.Modules.Trade.Views
{
    /// <summary>
    /// Root coordinator for the Trade screen.
    /// </summary>
    /// <remarks>
    /// <para>Receives <see cref="TradePresenter"/> by method injection, drives the sub-views, and
    /// forwards intents. Display-only: it never advances the offer timer or regeneration — both
    /// advance on the game clock (<c>IClock.Ticked</c>) so the mechanic keeps running on other tabs
    /// (<c>ARCHITECTURE.md</c> §4.5).</para>
    ///
    /// <para><b>Two different things happen in <see cref="Update"/>, and both are reads.</b> The
    /// refresh countdown is rewritten when its whole-second value changes, and the warehouse
    /// tiles are re-rendered because regeneration is continuous and deliberately publishes no
    /// event (see <c>KingdomLedger.AccruePassiveResourceRegen</c>). Every write underneath is
    /// dirty-checked, so a frame where nothing moved costs comparisons and no canvas rebuild.</para>
    ///
    /// <para>Canvas layout (TradeScreen.prefab) — each nested Canvas isolates something that
    /// redraws on its own schedule, and each needs its own GraphicRaycaster or its buttons go
    /// silently dead:</para>
    /// <code>
    /// TradeScreen            static chrome
    /// ├── WarehousePanel     nested — six fill bars moving continuously with regen
    /// ├── RefreshRow         nested — a countdown rewriting once a second
    /// ├── OfferListPanel     nested — a ScrollRect re-laying out on every drag frame
    /// └── ConfirmPanelCanvas nested, overrideSorting — draws above the rows it covers
    /// </code>
    /// </remarks>
    [RequireComponent(typeof(Canvas))]
    public sealed class TradeView : MonoBehaviour
    {
        [Header("Warehouses")]
        [Tooltip("All 6 tiles. Order does not matter — each declares which resource it renders.")]
        [SerializeField] private WarehouseTileView[] _warehouseTiles;

        [Header("Offers")]
        [Tooltip("Parent for the offer rows. Rows are created once from the prefab and reused.")]
        [SerializeField] private RectTransform _offerListContent;
        [SerializeField] private TradeOfferRowView _offerRowPrefab;
        [Tooltip("Shown when the offer list is empty.")]
        [SerializeField] private GameObject _noOffersLabel;

        [Header("Refresh")]
        [SerializeField] private TextMeshProUGUI _refreshCountdownLabel;
        [SerializeField] private Button _instantRefreshButton;
        [SerializeField] private TextMeshProUGUI _instantRefreshCostLabel;

        [Header("Confirmation")]
        [SerializeField] private TradeConfirmPanelView _confirmPanel;

        private TradePresenter _presenter;

        /// <summary>
        /// Tiles indexed by the resource each declares, so a designer reordering the serialized
        /// array cannot silently render one warehouse's data in another's tile
        /// (<c>docs/modules/Laws.md</c> §6.6).
        /// </summary>
        private readonly Dictionary<TradeResourceType, WarehouseTileView> _tilesByType = new();

        /// <summary>
        /// Rows are created once and repopulated, never destroyed and respawned. That is the
        /// pooling <c>ARCHITECTURE.md</c> §9 asks for without a pool class: the count is bounded
        /// by config and the list rebuilds every 20 minutes. The trigger for a real pool is an
        /// unbounded or paged list, or the floating "+50 Stone" popups on accept.
        /// </summary>
        private readonly List<TradeOfferRowView> _offerRows = new();

        private int    _shownCountdownSeconds = -1;
        private int    _shownRefreshCost      = -1;

        [Inject]
        public void Construct(TradePresenter presenter)
        {
            _presenter = presenter;
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        private void Start()
        {
            // Nothing injected us — almost always Play mode entered from Main instead of
            // Bootstrap, so the root LifetimeScope never existed. Say so once and switch off,
            // rather than throwing here and again from Update on every frame.
            if (_presenter == null)
            {
                Debug.LogError(
                    "[TradeView] No TradePresenter was injected — the Trade screen is disabled. " +
                    "Enter Play mode from the Bootstrap scene; Main is loaded additively from " +
                    "there and only then do its Views get injected.", this);
                enabled = false;
                return;
            }

            BuildTileLookup();
            ValidateSubViews();
            WireEvents();

            _presenter.OnStateChanged      += Refresh;
            _presenter.OnWarehouseUpgraded += HandleWarehouseUpgraded;

            // OnEnable may have run before injection completed; assert the real state now.
            _presenter.SetScreenVisible(isActiveAndEnabled);

            Refresh();
        }

        private void OnEnable()
        {
            // Injection happens before Start, but OnEnable can run first on the very first
            // frame — guard rather than assume ordering.
            _presenter?.SetScreenVisible(true);
        }

        private void OnDisable()
        {
            _presenter?.SetScreenVisible(false);
        }

        private void OnDestroy()
        {
            UnwireEvents();

            // The Presenter is a root-scope singleton that outlives this View, so a missed
            // unsubscribe leaks into the next scene load.
            if (_presenter == null) return;
            _presenter.OnStateChanged      -= Refresh;
            _presenter.OnWarehouseUpgraded -= HandleWarehouseUpgraded;
        }

        private void Update()
        {
            // Display only — the game clock is what advances both of these.
            RefreshCountdown();
            RefreshWarehouses();
        }

        // ── Rendering ─────────────────────────────────────────────────────────────

        private void Refresh()
        {
            RefreshWarehouses();
            RefreshOffers();
            RefreshRefreshButton();
            RefreshOpenConfirmPanel();
        }

        private void RefreshWarehouses()
        {
            foreach (var entry in _tilesByType)
                entry.Value.UpdateDisplay(_presenter.GetWarehouseDisplay(entry.Key));
        }

        private void RefreshOffers()
        {
            var displays = _presenter.GetOfferDisplays();
            EnsureRowCount(displays.Count);

            for (int i = 0; i < _offerRows.Count; i++)
            {
                if (i < displays.Count) _offerRows[i].Bind(displays[i]);
                else                    _offerRows[i].Hide();
            }

            if (_noOffersLabel != null) _noOffersLabel.SetActive(displays.Count == 0);
        }

        private void RefreshRefreshButton()
        {
            if (_instantRefreshButton != null)
                _instantRefreshButton.interactable = _presenter.CanAffordInstantRefresh;

            if (_instantRefreshCostLabel == null) return;

            int cost = _presenter.InstantRefreshCost;
            if (cost == _shownRefreshCost) return;

            _shownRefreshCost = cost;
            _instantRefreshCostLabel.SetText(cost.ToString());
        }

        private void RefreshCountdown()
        {
            if (_refreshCountdownLabel == null) return;

            // The countdown only ever shows whole seconds, so writing it every frame rewrites the
            // same string ~60x/second, dirtying the TMP mesh and rebuilding this canvas each time.
            int seconds = Mathf.CeilToInt(_presenter.SecondsUntilRefresh);
            if (seconds == _shownCountdownSeconds) return;

            _shownCountdownSeconds = seconds;
            _refreshCountdownLabel.SetText(_presenter.GetRefreshCountdownText());
        }

        /// <summary>
        /// Keep an open panel in step with the model — the offer under it may have been refreshed
        /// away, or an upgrade may have changed what fits.
        /// </summary>
        private void RefreshOpenConfirmPanel()
        {
            if (_confirmPanel == null || !_confirmPanel.IsOpen) return;

            if (_presenter.TryGetConfirmation(_confirmPanel.OfferId, out var display))
                _confirmPanel.Open(display);
            else
                _confirmPanel.Close();   // the offer is gone
        }

        // ── Intents ───────────────────────────────────────────────────────────────

        private void HandleOfferPressed(string offerId)
        {
            // Always opens, even for an offer that cannot be afforded: GDD §7 requires the panel
            // to explain rather than the row to be silently disabled.
            if (_confirmPanel != null && _presenter.TryGetConfirmation(offerId, out var display))
                _confirmPanel.Open(display);
        }

        private void HandleConfirmPressed(string offerId)
        {
            // Business logic first; the panel closes only on success so a refusal stays readable.
            if (_presenter.OnOfferAcceptRequested(offerId))
                _confirmPanel.Close();
        }

        private void HandleCancelPressed() => _confirmPanel.Close();

        private void HandleInstantRefreshPressed() => _presenter.OnInstantRefreshRequested();

        private void HandleCrystalUpgradePressed(TradeResourceType type) =>
            _presenter.OnCrystalUpgradeRequested(type);

        private void HandlePairedUpgradePressed(TradeResourceType type) =>
            _presenter.OnPairedUpgradeRequested(type);

        private void HandleWarehouseUpgraded(TradeResourceType type)
        {
            if (_tilesByType.TryGetValue(type, out var tile)) tile.PlayUpgradeCelebration();
        }

        // ── Wiring ────────────────────────────────────────────────────────────────

        private void WireEvents()
        {
            // Method groups, not lambdas: a lambda creates a fresh delegate each time and could
            // never be unsubscribed in OnDestroy.
            foreach (var tile in _tilesByType.Values)
            {
                tile.OnCrystalUpgradePressed += HandleCrystalUpgradePressed;
                tile.OnPairedUpgradePressed  += HandlePairedUpgradePressed;
            }

            if (_instantRefreshButton != null)
                _instantRefreshButton.onClick.AddListener(HandleInstantRefreshPressed);

            if (_confirmPanel != null)
            {
                _confirmPanel.OnConfirmPressed += HandleConfirmPressed;
                _confirmPanel.OnCancelPressed  += HandleCancelPressed;
            }
        }

        private void UnwireEvents()
        {
            foreach (var tile in _tilesByType.Values)
            {
                if (tile == null) continue;
                tile.OnCrystalUpgradePressed -= HandleCrystalUpgradePressed;
                tile.OnPairedUpgradePressed  -= HandlePairedUpgradePressed;
            }

            foreach (var row in _offerRows)
                if (row != null) row.OnPressed -= HandleOfferPressed;

            if (_instantRefreshButton != null)
                _instantRefreshButton.onClick.RemoveListener(HandleInstantRefreshPressed);

            if (_confirmPanel != null)
            {
                _confirmPanel.OnConfirmPressed -= HandleConfirmPressed;
                _confirmPanel.OnCancelPressed  -= HandleCancelPressed;
            }
        }

        /// <summary>Grow the row pool to cover the current list. Rows are never destroyed.</summary>
        private void EnsureRowCount(int needed)
        {
            if (_offerRowPrefab == null || _offerListContent == null) return;

            while (_offerRows.Count < needed)
            {
                var row = Instantiate(_offerRowPrefab, _offerListContent);
                row.OnPressed += HandleOfferPressed;
                _offerRows.Add(row);
            }
        }

        private void BuildTileLookup()
        {
            _tilesByType.Clear();
            if (_warehouseTiles == null) return;

            foreach (var tile in _warehouseTiles)
            {
                if (tile == null) continue;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (_tilesByType.ContainsKey(tile.Type))
                    Debug.LogError(
                        $"[TradeView] Two warehouse tiles are both bound to {tile.Type}. Each " +
                        "tile's Type must be unique — one resource will not be shown.", this);
#endif
                _tilesByType[tile.Type] = tile;
            }
        }

        private void ValidateSubViews()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_offerListContent == null)
                Debug.LogError("[TradeView] _offerListContent is not assigned.", this);
            if (_offerRowPrefab == null)
                Debug.LogError("[TradeView] _offerRowPrefab is not assigned.", this);
            if (_refreshCountdownLabel == null)
                Debug.LogError("[TradeView] _refreshCountdownLabel is not assigned.", this);
            if (_instantRefreshButton == null)
                Debug.LogError("[TradeView] _instantRefreshButton is not assigned.", this);
            if (_confirmPanel == null)
                Debug.LogError("[TradeView] _confirmPanel is not assigned.", this);

            // Coverage by type, not array length: six tiles all pointing at Stone would pass a
            // length check and render nonsense.
            foreach (TradeResourceType type in Enum.GetValues(typeof(TradeResourceType)))
                if (!_tilesByType.ContainsKey(type))
                    Debug.LogError($"[TradeView] No WarehouseTileView is bound to {type}.", this);
#endif
        }
    }
}
