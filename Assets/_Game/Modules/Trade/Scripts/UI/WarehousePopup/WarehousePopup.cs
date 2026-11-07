using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using KingdomRuler.Systems.Popups;

namespace KingdomRuler.Modules.Trade.UI
{
    /// <summary>
    /// One resource's warehouse, opened by tapping its tile: level, stock / capacity, how regeneration
    /// is going, and the two upgrade options with their prices. Stays open across upgrades.
    /// </summary>
    /// <remarks>
    /// The choice is the upgrade button pressed. Both are always tappable below the cap — whether the
    /// player can afford it is shown in the confirmation. No close button: a tap outside closes it.
    /// There is deliberately no "collect": regenerated units go straight into the warehouse (GDD §7).
    /// </remarks>
    public sealed class WarehousePopup : Popup<WarehousePopupData, WarehousePayment>
    {
        [Header("Warehouse")]
        [SerializeField] private TextMeshProUGUI _titleLabel;
        [SerializeField] private Image           _icon;
        [SerializeField] private TextMeshProUGUI _levelLabel;
        [SerializeField] private TextMeshProUGUI _stockLabel;
        [Tooltip("Image type Filled, fillMethod Horizontal.")]
        [SerializeField] private Image           _stockFill;

        [Header("Regeneration")]
        [Tooltip("Holds the next-unit bar; hidden while full or when a unit takes under a second.")]
        [SerializeField] private GameObject      _nextUnitRoot;
        [Tooltip("Image type Filled, fillMethod Horizontal.")]
        [SerializeField] private Image           _nextUnitFill;
        [SerializeField] private TextMeshProUGUI _rateLabel;
        [SerializeField] private TextMeshProUGUI _fullLabel;

        [Header("Upgrades")]
        [SerializeField] private Button          _crystalButton;
        [SerializeField] private TextMeshProUGUI _crystalPriceLabel;
        [SerializeField] private Button          _pairedButton;
        [SerializeField] private TextMeshProUGUI _pairedPriceLabel;
        [Tooltip("\"Fully upgraded\", shown in place of the prices at the cap.")]
        [SerializeField] private TextMeshProUGUI _maxedLabel;

        [Header("Animation")]
        [SerializeField] private float _fillDuration  = 0.3f;
        [SerializeField] private float _punchScale    = 0.3f;
        [SerializeField] private float _punchDuration = 0.4f;

        private Tween _fillTween;
        private Tween _punchTween;
        private int   _shownLevel;

        private void Awake()
        {
            _crystalButton.onClick.AddListener(() => Choose(WarehousePayment.Crystals));
            _pairedButton.onClick.AddListener(() => Choose(WarehousePayment.PairedResource));
        }

        protected override void OnDestroy()
        {
            _fillTween?.Kill();
            _punchTween?.Kill();
            base.OnDestroy();
        }

        protected override void Render(WarehousePopupData data)
        {
            _titleLabel.SetText(data.Title);
            _icon.sprite  = data.Icon;
            _icon.enabled = data.Icon != null;   // an Image without a sprite draws a white box
            _levelLabel.SetText(data.LevelText);
            _stockLabel.SetText($"{data.Stock:N0} / {data.Capacity:N0}");

            _nextUnitRoot.SetActive(data.ShowsNextUnit);
            _nextUnitFill.fillAmount = data.NextUnitPercent / 100f;
            _rateLabel.SetText(data.RateText);
            _fullLabel.SetText(data.FullText);

            _crystalButton.interactable = data.CanUpgrade;
            _pairedButton.interactable  = data.CanUpgrade;
            _crystalPriceLabel.SetText(data.CrystalPrice ?? string.Empty);
            _pairedPriceLabel.SetText(data.PairedPrice ?? string.Empty);
            _maxedLabel.gameObject.SetActive(!data.CanUpgrade);
            if (!data.CanUpgrade) _maxedLabel.SetText(data.MaxedText);

            bool firstDraw = _shownLevel == 0;
            _fillTween?.Kill();
            if (firstDraw) _stockFill.fillAmount = data.Fill;
            else _fillTween = _stockFill.DOFillAmount(data.Fill, _fillDuration).SetEase(Ease.OutQuad).SetUpdate(true);

            if (!firstDraw && data.DisplayedLevel > _shownLevel) PlayLevelUp();
            _shownLevel = data.DisplayedLevel;
        }

        private void PlayLevelUp()
        {
            _punchTween?.Kill(complete: true);
            _punchTween = _levelLabel.transform
                .DOPunchScale(Vector3.one * _punchScale, _punchDuration, 6, 0.4f)
                .SetUpdate(true);
        }
    }
}
