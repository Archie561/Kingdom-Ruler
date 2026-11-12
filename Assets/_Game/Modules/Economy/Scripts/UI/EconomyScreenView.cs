using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;
using KingdomRuler.Shared.Text;

namespace KingdomRuler.Modules.Economy.UI
{
    /// <summary>
    /// The Economy screen: the total gold at the top and one row per business below. Renders the
    /// presenter's state and forwards taps — a row opens its panel, its Collect button collects.
    /// </summary>
    /// <remarks>
    /// It polls every row and the gold total every frame and writes only what changed: storages fill
    /// continuously and publish nothing (<c>ARCHITECTURE.md</c> §4.5). Text is asked for only when its
    /// <see cref="NumberFormat.DisplayedValue"/> changes, so steady polling formats nothing.
    /// </remarks>
    [RequireComponent(typeof(Canvas))]
    public sealed class EconomyScreenView : MonoBehaviour
    {
        [Tooltip("One row per business. Matched by each row's own BusinessId, so order does not matter.")]
        [SerializeField] private BusinessRowView[] _rows;

        [Tooltip("Temporary, until the currency header exists — see GoldTotalView.")]
        [SerializeField] private GoldTotalView _goldTotal;

        private readonly Dictionary<string, BusinessRowView> _rowById = new();
        private EconomyPresenter _presenter;

        [Inject]
        public void Construct(EconomyPresenter presenter) => _presenter = presenter;

        private void Start()
        {
            if (_presenter == null)
            {
                Debug.LogError("[EconomyScreenView] No EconomyPresenter injected — enter Play mode from the " +
                               "Bootstrap scene, which loads Main and injects its views.", this);
                enabled = false;
                return;
            }

            BindRows();

            foreach (var row in _rows)
            {
                row.Pressed        += id => _presenter.OpenBusiness(id).Forget();
                row.CollectPressed += id => _presenter.Collect(id);
            }

            _presenter.StateChanged   += Refresh;
            _presenter.Collected      += PlayCollected;
            _presenter.BusinessBought += PlayBought;

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
            _presenter.StateChanged   -= Refresh;
            _presenter.Collected      -= PlayCollected;
            _presenter.BusinessBought -= PlayBought;
        }

        private void Update()
        {
            foreach (var pair in _rowById) RenderRow(pair.Key, pair.Value);

            long gold = NumberFormat.DisplayedValue(_presenter.Gold);
            if (gold != _goldTotal.ShownDisplay) _goldTotal.Show(gold, _presenter.Amount(_presenter.Gold));
        }

        /// <summary>The language changed, or the screen came back: re-resolve every text.</summary>
        private void Refresh()
        {
            foreach (var pair in _rowById)
            {
                pair.Value.SetName(_presenter.BusinessName(pair.Key));
                pair.Value.InvalidateText();
                RenderRow(pair.Key, pair.Value);
            }
            _goldTotal.InvalidateText();
        }

        private void RenderRow(string businessId, BusinessRowView row)
        {
            var data = _presenter.GetRow(businessId);
            row.Render(data);

            if (data.UnitsOwned != row.ShownUnits)
                row.SetOwned(data.UnitsOwned, _presenter.UnitsOwnedText(data.UnitsOwned));

            long stored   = NumberFormat.DisplayedValue(data.Stored);
            long capacity = NumberFormat.DisplayedValue(data.Capacity);
            if (stored != row.ShownStoredDisplay || capacity != row.ShownCapacityDisplay)
                row.SetStorage(stored, capacity, _presenter.Amount(data.Stored) + " / " + _presenter.Amount(data.Capacity));
        }

        private void PlayCollected(string businessId, long amount)
        {
            if (_rowById.TryGetValue(businessId, out var row)) row.PlayCollected("+" + _presenter.Amount(amount));
        }

        private void PlayBought(string businessId)
        {
            if (_rowById.TryGetValue(businessId, out var row)) row.PlayBought();
        }

        /// <summary>Every business needs exactly one row, and every row a business; a mismatch is logged, not guessed at.</summary>
        private void BindRows()
        {
            var known = new HashSet<string>(_presenter.BusinessIds);
            foreach (var row in _rows)
            {
                if (row == null) continue;
                if (!known.Contains(row.BusinessId))
                    Debug.LogError($"[EconomyScreenView] Row '{row.name}' shows '{row.BusinessId}', which is not a business; it stays blank.", row);
                else if (_rowById.ContainsKey(row.BusinessId))
                    Debug.LogError($"[EconomyScreenView] Two rows show '{row.BusinessId}'; the first is used.", row);
                else
                    _rowById.Add(row.BusinessId, row);
            }

            foreach (var id in _presenter.BusinessIds)
                if (!_rowById.ContainsKey(id))
                    Debug.LogError($"[EconomyScreenView] No row shows '{id}'; there must be exactly one.", this);
        }
    }
}
