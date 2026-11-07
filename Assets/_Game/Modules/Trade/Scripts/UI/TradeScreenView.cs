using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Trade.UI
{
    /// <summary>
    /// The Trade screen: six resource tiles always on screen, and below them the offer list with the
    /// refresh countdown and button in its header. Renders the presenter's state and forwards taps.
    /// </summary>
    /// <remarks>
    /// <para>It polls the tiles and the countdown every frame and writes only what changed: stock
    /// moves with regeneration, which publishes no event (<c>ARCHITECTURE.md</c> §4.5). Text — names,
    /// levels, the countdown — is asked for only when its number changes, so steady polling
    /// allocates nothing. It displays the refresh timer but never advances it.</para>
    /// </remarks>
    [RequireComponent(typeof(Canvas))]
    public sealed class TradeScreenView : MonoBehaviour
    {
        [Header("Warehouses")]
        [Tooltip("One tile per resource. Matched by each tile's own Type, so order does not matter.")]
        [SerializeField] private ResourceTileView[] _tiles;

        [Header("Offers")]
        [SerializeField] private OfferListView   _offerList;
        [SerializeField] private TextMeshProUGUI _countdownLabel;
        [SerializeField] private Button          _refreshButton;
        [SerializeField] private TextMeshProUGUI _refreshCostLabel;

        [Header("Receipt")]
        [SerializeField] private TradeReceiptView _receipt;

        private TradePresenter _presenter;

        // Last values written, so a frame where nothing changed rewrites nothing.
        private int _shownCountdown  = -1;
        private int _shownRefreshCost = -1;

        [Inject]
        public void Construct(TradePresenter presenter) => _presenter = presenter;

        private void Start()
        {
            if (_presenter == null)
            {
                Debug.LogError("[TradeScreenView] No TradePresenter injected — enter Play mode from the " +
                               "Bootstrap scene, which loads Main and injects its views.", this);
                enabled = false;
                return;
            }

            CheckTilesCoverEveryResource();

            foreach (var tile in _tiles) tile.Pressed += type => _presenter.OpenWarehouse(type).Forget();
            _offerList.OfferPressed += id => _presenter.OpenOffer(id).Forget();
            _refreshButton.onClick.AddListener(() => _presenter.RequestInstantRefresh().Forget());

            _presenter.StateChanged      += Refresh;
            _presenter.OffersArrived     += _offerList.PlayArrival;
            _presenter.TradeCompleted    += _receipt.Show;
            _presenter.WarehouseUpgraded += PlayUpgrade;

            // OnEnable may have run before injection.
            _presenter.SetScreenVisible(isActiveAndEnabled);
            Refresh();
        }

        private void OnEnable()  => _presenter?.SetScreenVisible(true);
        private void OnDisable() => _presenter?.SetScreenVisible(false);

        // The presenter outlives this view, so its events must be released.
        private void OnDestroy()
        {
            if (_presenter == null) return;
            _presenter.StateChanged      -= Refresh;
            _presenter.OffersArrived     -= _offerList.PlayArrival;
            _presenter.TradeCompleted    -= _receipt.Show;
            _presenter.WarehouseUpgraded -= PlayUpgrade;
        }

        private void Update()
        {
            int seconds = Mathf.CeilToInt(_presenter.SecondsUntilRefresh);
            if (seconds != _shownCountdown)
            {
                _shownCountdown = seconds;
                _countdownLabel.SetText(_presenter.RefreshCountdownText(seconds));
            }

            foreach (var tile in _tiles) RenderTile(tile);
        }

        /// <summary>The offer list or the language changed.</summary>
        private void Refresh()
        {
            foreach (var tile in _tiles)
            {
                tile.SetName(_presenter.ResourceName(tile.Type));
                var data = _presenter.GetTile(tile.Type);
                tile.SetLevel(data.DisplayedLevel, _presenter.LevelText(data.DisplayedLevel));
                tile.Render(data);
            }

            _shownCountdown = -1;   // re-resolve the countdown, in case the language changed

            int cost = _presenter.InstantRefreshCost;
            if (cost != _shownRefreshCost)
            {
                _shownRefreshCost = cost;
                _refreshCostLabel.SetText(cost.ToString());
            }

            _offerList.Show(_presenter.GetOffers());
        }

        private void RenderTile(ResourceTileView tile)
        {
            var data = _presenter.GetTile(tile.Type);
            if (data.DisplayedLevel != tile.ShownLevel) tile.SetLevel(data.DisplayedLevel, _presenter.LevelText(data.DisplayedLevel));
            tile.Render(data);
        }

        private void PlayUpgrade(TradeResourceType type)
        {
            foreach (var tile in _tiles)
                if (tile.Type == type)
                {
                    RenderTile(tile);
                    tile.PlayUpgrade();
                }
        }

        private void CheckTilesCoverEveryResource()
        {
            foreach (TradeResourceType type in Enum.GetValues(typeof(TradeResourceType)))
            {
                int count = 0;
                foreach (var tile in _tiles)
                    if (tile.Type == type) count++;

                if (count != 1)
                    Debug.LogError($"[TradeScreenView] {count} tiles show {type}; there must be exactly one.", this);
            }
        }
    }
}
