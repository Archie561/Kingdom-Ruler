using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using KingdomRuler.Shared.Ledger;
using KingdomRuler.Modules.Trade.Presenters;

namespace KingdomRuler.Modules.Trade.Views
{
    /// <summary>
    /// One warehouse: its fill bar, level, and the two upgrade paths.
    /// </summary>
    /// <remarks>
    /// <para><b>Declares which resource it renders</b> via <see cref="_type"/>, so
    /// <see cref="TradeView"/> can bind tiles into a dictionary by type rather than by array
    /// position. Reordering the serialized array is therefore harmless — the trap
    /// <c>docs/modules/Laws.md</c> §6.6 records is that index binding silently renders one
    /// resource's data in another's tile while everything still appears to work.</para>
    ///
    /// <para>The fill is refreshed every frame by <see cref="TradeView"/> because warehouse
    /// regeneration is continuous and deliberately publishes no event. Writes are guarded by a
    /// dirty-check so an unchanged value never dirties the canvas.</para>
    /// </remarks>
    public sealed class WarehouseTileView : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Which resource this tile renders. Must be unique across the tiles on the screen.")]
        [SerializeField] private TradeResourceType _type;

        [Header("Sub-views")]
        [SerializeField] private Image _icon;
        [SerializeField] private TextMeshProUGUI _nameLabel;
        [SerializeField] private Image _fillBar;
        [SerializeField] private TextMeshProUGUI _amountLabel;
        [SerializeField] private TextMeshProUGUI _levelLabel;

        [Header("Upgrades")]
        [SerializeField] private Button _crystalUpgradeButton;
        [SerializeField] private TextMeshProUGUI _crystalCostLabel;
        [SerializeField] private Button _pairedUpgradeButton;
        [SerializeField] private TextMeshProUGUI _pairedCostLabel;

        [Header("Feel")]
        [SerializeField] private float _fillTweenDuration = 0.25f;

        private Tween _fillTween;
        private Tween _punchTween;

        // Last values pushed to the UI, so a refresh doesn't re-issue identical strings.
        private bool   _hasRendered;
        private float  _renderedFill      = -1f;
        private string _renderedAmount;
        private int    _renderedLevel     = -1;
        private string _renderedCrystalCost;
        private string _renderedPairedCost;

        /// <summary>Which resource this tile renders. What TradeView keys its lookup on.</summary>
        public TradeResourceType Type => _type;

        public event Action<TradeResourceType> OnCrystalUpgradePressed;
        public event Action<TradeResourceType> OnPairedUpgradePressed;

        private void Awake()
        {
            if (_crystalUpgradeButton != null)
                _crystalUpgradeButton.onClick.AddListener(HandleCrystalPressed);
            if (_pairedUpgradeButton != null)
                _pairedUpgradeButton.onClick.AddListener(HandlePairedPressed);
        }

        private void OnDestroy()
        {
            if (_crystalUpgradeButton != null)
                _crystalUpgradeButton.onClick.RemoveListener(HandleCrystalPressed);
            if (_pairedUpgradeButton != null)
                _pairedUpgradeButton.onClick.RemoveListener(HandlePairedPressed);

            // A tween outliving the object it animates throws on its next step.
            _fillTween?.Kill();
            _punchTween?.Kill();
        }

        /// <summary>Render this tile. Cheap to call every frame — unchanged values are skipped.</summary>
        public void UpdateDisplay(WarehouseDisplayData data)
        {
            if (_icon != null)
            {
                // A UGUI Image with no sprite draws a filled white rectangle, so hide the slot
                // instead while art is outstanding.
                _icon.enabled = data.Icon != null;
                if (data.Icon != null) _icon.sprite = data.Icon;
            }

            if (_nameLabel != null && !_hasRendered) _nameLabel.SetText(data.Name);

            if (_fillBar != null && !Mathf.Approximately(_renderedFill, data.FillFraction))
            {
                _fillTween?.Kill();
                if (_hasRendered)
                    _fillTween = _fillBar.DOFillAmount(data.FillFraction, _fillTweenDuration)
                                         .SetEase(Ease.OutQuad).SetUpdate(true);
                else
                    _fillBar.fillAmount = data.FillFraction;

                _renderedFill = data.FillFraction;
            }

            if (_amountLabel != null && data.AmountText != _renderedAmount)
            {
                _renderedAmount = data.AmountText;
                _amountLabel.SetText(data.AmountText);
            }

            if (_levelLabel != null && data.Level != _renderedLevel)
            {
                _renderedLevel = data.Level;
                _levelLabel.SetText(data.Level.ToString());
            }

            if (_crystalCostLabel != null && data.CrystalCostText != _renderedCrystalCost)
            {
                _renderedCrystalCost = data.CrystalCostText;
                _crystalCostLabel.SetText(data.CrystalCostText);
            }

            if (_pairedCostLabel != null && data.PairedCostText != _renderedPairedCost)
            {
                _renderedPairedCost = data.PairedCostText;
                _pairedCostLabel.SetText(data.PairedCostText);
            }

            if (_crystalUpgradeButton != null)
                _crystalUpgradeButton.interactable = data.CanAffordCrystalUpgrade;
            if (_pairedUpgradeButton != null)
                _pairedUpgradeButton.interactable = data.CanAffordPairedUpgrade;

            _hasRendered = true;
        }

        /// <summary>Celebrate an upgrade landing. Purely cosmetic.</summary>
        public void PlayUpgradeCelebration()
        {
            _punchTween?.Kill(complete: true);
            // Unscaled so it stays in sync if anything ever zeroes Time.timeScale.
            _punchTween = transform.DOPunchScale(Vector3.one * 0.15f, 0.35f, 8, 0.6f)
                                   .SetUpdate(true);
        }

        private void HandleCrystalPressed() => OnCrystalUpgradePressed?.Invoke(_type);
        private void HandlePairedPressed()  => OnPairedUpgradePressed?.Invoke(_type);
    }
}
