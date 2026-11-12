using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using KingdomRuler.Systems.Popups;

namespace KingdomRuler.Modules.Economy.UI
{
    /// <summary>
    /// One business's panel, opened by tapping its row (GDD §8): artwork, flavor, units owned, the rate,
    /// the storage with Collect, and Buy ×1 / ×10 / Max. Stays open across purchases and collections.
    /// </summary>
    /// <remarks>
    /// <para>The choice is the button pressed. A buy the gold does not cover stays tappable — the
    /// Presenter answers it with the crystal top-up — and its price is only drawn muted. At the units
    /// ceiling the buy buttons are inactive and "Maximum owned" replaces them. No close button: a tap
    /// outside closes it.</para>
    ///
    /// <para>The Presenter that opened it calls <see cref="PlayCollected"/> and <see cref="PlayBought"/>
    /// on it: the panel cannot tell a collect from a coin landing by its numbers alone.</para>
    /// </remarks>
    public sealed class BusinessPopup : Popup<BusinessPopupData, BusinessPopupChoice>
    {
        [Header("Business")]
        [SerializeField] private Image           _artwork;
        [SerializeField] private TextMeshProUGUI _nameLabel;
        [SerializeField] private TextMeshProUGUI _ownedLabel;
        [SerializeField] private TextMeshProUGUI _rateLabel;
        [SerializeField] private TextMeshProUGUI _flavorLabel;

        [Header("Storage")]
        [Tooltip("Image type Filled, fillMethod Horizontal, with a sprite — without one Unity draws a full bar.")]
        [SerializeField] private Image           _storageFill;
        [SerializeField] private TextMeshProUGUI _storageLabel;
        [SerializeField] private Button          _collectButton;

        [Header("Buying")]
        [SerializeField] private Button          _buyOneButton;
        [SerializeField] private TextMeshProUGUI _onePriceLabel;
        [SerializeField] private Button          _buyTenButton;
        [SerializeField] private TextMeshProUGUI _tenPriceLabel;
        [SerializeField] private Button          _buyMaxButton;
        [Tooltip("\"Max ×7\": the count changes with the gold, so it is set from code.")]
        [SerializeField] private TextMeshProUGUI _maxCaptionLabel;
        [SerializeField] private TextMeshProUGUI _maxPriceLabel;
        [Tooltip("\"Maximum owned\", shown in place of the buy buttons at the units ceiling.")]
        [SerializeField] private TextMeshProUGUI _maxedLabel;
        [SerializeField] private Color           _affordableColor   = Color.white;
        [SerializeField] private Color           _unaffordableColor = new Color(0.62f, 0.52f, 0.52f, 1f);

        [Header("Feel")]
        [Tooltip("\"+1.23K\" rising from the storage bar on a collect. Starts hidden.")]
        [SerializeField] private TextMeshProUGUI _floatLabel;
        [SerializeField] private CanvasGroup     _floatGroup;
        [SerializeField] private float _fillDuration  = 0.3f;
        [SerializeField] private float _punchScale    = 0.25f;
        [SerializeField] private float _punchDuration = 0.4f;
        [SerializeField] private float _floatRise     = 90f;
        [SerializeField] private float _floatDuration = 0.9f;

        private Tween    _fillTween;
        private Tween    _punchTween;
        private Sequence _floatSequence;
        private Vector2  _floatRestPosition;
        private bool     _hasRendered;

        private void Awake()
        {
            _collectButton.onClick.AddListener(() => Choose(BusinessPopupChoice.Collect));
            _buyOneButton.onClick.AddListener(() => Choose(BusinessPopupChoice.BuyOne));
            _buyTenButton.onClick.AddListener(() => Choose(BusinessPopupChoice.BuyTen));
            _buyMaxButton.onClick.AddListener(() => Choose(BusinessPopupChoice.BuyMax));

            _floatRestPosition = _floatLabel.rectTransform.anchoredPosition;
            _floatGroup.alpha  = 0f;
        }

        protected override void OnDestroy()
        {
            _fillTween?.Kill();
            _punchTween?.Kill();
            _floatSequence?.Kill();
            base.OnDestroy();
        }

        protected override void Render(BusinessPopupData data)
        {
            // An Image without a sprite draws a white box, so hide it instead.
            _artwork.sprite  = data.Artwork;
            _artwork.enabled = data.Artwork != null;

            _nameLabel.SetText(data.Name);
            _ownedLabel.SetText(data.OwnedText);
            _rateLabel.SetText(data.RateText);
            _flavorLabel.SetText(data.Flavor);

            _storageLabel.SetText(data.StoredText + " / " + data.CapacityText);
            RenderFill(data.Fill);
            _collectButton.interactable = data.CanCollect;

            bool maxed = data.MaxedText != null;
            _buyOneButton.gameObject.SetActive(!maxed);
            _buyTenButton.gameObject.SetActive(!maxed);
            _buyMaxButton.gameObject.SetActive(!maxed);
            _maxedLabel.gameObject.SetActive(maxed);
            if (maxed)
            {
                _maxedLabel.SetText(data.MaxedText);
                return;
            }

            _buyOneButton.interactable = data.CanBuyOne;
            _buyTenButton.interactable = data.CanBuyTen;
            _buyMaxButton.interactable = data.CanBuyMax;
            ShowPrice(_onePriceLabel, data.OnePrice, data.AffordsOne);
            ShowPrice(_tenPriceLabel, data.TenPrice, data.AffordsTen);
            ShowPrice(_maxPriceLabel, data.MaxPrice, data.AffordsMax);
            _maxCaptionLabel.SetText(data.MaxText);
        }

        /// <summary>Gold was collected from this business: a "+1.23K" rises from the storage bar.</summary>
        public void PlayCollected(string amountText)
        {
            _floatSequence?.Kill(complete: true);

            var rect = _floatLabel.rectTransform;
            _floatLabel.SetText(amountText);
            rect.anchoredPosition = _floatRestPosition;
            _floatGroup.alpha     = 1f;

            _floatSequence = DOTween.Sequence()
                .Append(rect.DOAnchorPos(_floatRestPosition + Vector2.up * _floatRise, _floatDuration).SetEase(Ease.OutCubic))
                .Join(_floatGroup.DOFade(0f, _floatDuration).SetEase(Ease.InQuad))
                .SetUpdate(true);
        }

        /// <summary>Units were bought: the owned count punches.</summary>
        public void PlayBought()
        {
            _punchTween?.Kill(complete: true);
            _punchTween = _ownedLabel.transform
                .DOPunchScale(Vector3.one * _punchScale, _punchDuration, 6, 0.4f)
                .SetUpdate(true);
        }

        private void ShowPrice(TextMeshProUGUI label, string price, bool affordable)
        {
            label.SetText(price ?? string.Empty);
            label.color = affordable ? _affordableColor : _unaffordableColor;
        }

        /// <summary>A collect or a purchase moves the bar visibly and tweens; a coin landing just sets it.</summary>
        private void RenderFill(float fill)
        {
            if (!_hasRendered)
            {
                _storageFill.fillAmount = fill;
                _hasRendered = true;
                return;
            }

            if (Mathf.Abs(fill - _storageFill.fillAmount) < 0.02f && (_fillTween == null || !_fillTween.IsActive()))
            {
                _storageFill.fillAmount = fill;
                return;
            }

            _fillTween?.Kill();
            _fillTween = _storageFill.DOFillAmount(fill, _fillDuration).SetEase(Ease.OutQuad).SetUpdate(true);
        }
    }
}
