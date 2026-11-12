using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KingdomRuler.Modules.Economy.UI
{
    /// <summary>
    /// One business on the Economy screen: artwork, name, units owned, the storage fill, a "Full" marker
    /// and a Collect button. A tap on Collect collects; a tap anywhere else on the row opens the
    /// business's panel.
    /// </summary>
    /// <remarks>
    /// <para><b>Two tap targets, one row.</b> The Collect button is a child drawn over the row's own
    /// button, so a tap on it reaches only Collect — the click handler is the nearest one above the
    /// graphic hit — and never opens the panel.</para>
    ///
    /// <para>It draws numbers only; the screen view hands it text when the number in it changes, so
    /// steady polling formats nothing.</para>
    /// </remarks>
    public sealed class BusinessRowView : MonoBehaviour
    {
        [Tooltip("The business this row shows — its BusinessId. Set per instance.")]
        [SerializeField] private string _businessId;

        [Tooltip("Covers the whole row, over a raycast-target background. Opens the panel.")]
        [SerializeField] private Button _button;

        [Tooltip("Drawn over the row's button; shown while the storage holds gold.")]
        [SerializeField] private Button _collectButton;

        [Header("Display")]
        [SerializeField] private Image           _artwork;
        [SerializeField] private TextMeshProUGUI _nameLabel;
        [SerializeField] private TextMeshProUGUI _ownedLabel;
        [SerializeField] private TextMeshProUGUI _storageLabel;
        [Tooltip("Image type Filled, fillMethod Horizontal, with a sprite — without one Unity draws a full bar.")]
        [SerializeField] private Image           _fillBar;
        [SerializeField] private GameObject      _fullMarker;

        [Header("Feel")]
        [Tooltip("\"+1.23K\" rising from the bar on a collect. Starts hidden.")]
        [SerializeField] private TextMeshProUGUI _floatLabel;
        [SerializeField] private CanvasGroup     _floatGroup;
        [SerializeField] private float _fillDuration  = 0.3f;
        [SerializeField] private float _punchScale    = 0.25f;
        [SerializeField] private float _punchDuration = 0.4f;
        [SerializeField] private float _floatRise     = 70f;
        [SerializeField] private float _floatDuration = 0.9f;

        private Tween    _fillTween;
        private Tween    _ownedPunch;
        private Tween    _fullPunch;
        private Sequence _floatSequence;
        private Vector2  _floatRestPosition;

        // Last values drawn, so steady polling rewrites nothing and dirties nothing.
        private bool   _hasRendered;
        private long   _shownStored, _shownCapacity;
        private bool   _shownFull, _shownCollect;
        private Sprite _shownArtwork;

        public string BusinessId => _businessId;

        /// <summary>The units count whose text is on the label; -1 before any. The screen view refreshes the text when it differs.</summary>
        public int ShownUnits { get; private set; } = -1;

        /// <summary>The displayed storage values on the label; the screen view refreshes the text when either differs.</summary>
        public long ShownStoredDisplay   { get; private set; } = long.MinValue;
        public long ShownCapacityDisplay { get; private set; } = long.MinValue;

        public event Action<string> Pressed;
        public event Action<string> CollectPressed;

        private void Awake()
        {
            _button.onClick.AddListener(() => Pressed?.Invoke(_businessId));
            _collectButton.onClick.AddListener(() => CollectPressed?.Invoke(_businessId));

            _floatRestPosition = _floatLabel.rectTransform.anchoredPosition;
            _floatGroup.alpha  = 0f;
        }

        // A tab switch mid-animation must not leave a half-faded float or a stretched label behind.
        private void OnDisable()
        {
            _fillTween?.Kill(complete: true);
            _ownedPunch?.Kill(complete: true);
            _fullPunch?.Kill(complete: true);
            _floatSequence?.Kill();
            if (_floatGroup != null) _floatGroup.alpha = 0f;
        }

        /// <summary>Draw the numbers, rewriting only what changed.</summary>
        public void Render(BusinessRowViewData data)
        {
            if (!_hasRendered || data.Artwork != _shownArtwork)
            {
                // An Image without a sprite draws a white box, so hide it instead.
                _artwork.sprite  = data.Artwork;
                _artwork.enabled = data.Artwork != null;
                _shownArtwork    = data.Artwork;
            }

            if (!_hasRendered || data.CanCollect != _shownCollect)
            {
                _collectButton.gameObject.SetActive(data.CanCollect);
                _shownCollect = data.CanCollect;
            }

            if (!_hasRendered || data.IsFull != _shownFull)
            {
                _fullMarker.SetActive(data.IsFull);
                if (_hasRendered && data.IsFull) PopFullMarker();
                _shownFull = data.IsFull;
            }

            if (!_hasRendered || data.Stored != _shownStored || data.Capacity != _shownCapacity)
            {
                RenderFill(data.Fill);
                _shownStored   = data.Stored;
                _shownCapacity = data.Capacity;
            }

            _hasRendered = true;
        }

        public void SetName(string name) => _nameLabel.SetText(name);

        public void SetOwned(int unitsOwned, string text)
        {
            ShownUnits = unitsOwned;
            _ownedLabel.SetText(text);
        }

        public void SetStorage(long storedDisplay, long capacityDisplay, string text)
        {
            ShownStoredDisplay   = storedDisplay;
            ShownCapacityDisplay = capacityDisplay;
            _storageLabel.SetText(text);
        }

        /// <summary>Forget the text on the labels, so the next render re-resolves it (a language change).</summary>
        public void InvalidateText()
        {
            ShownUnits           = -1;
            ShownStoredDisplay   = long.MinValue;
            ShownCapacityDisplay = long.MinValue;
        }

        /// <summary>Gold was collected from this business: a "+1.23K" rises from the bar.</summary>
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
            _ownedPunch?.Kill(complete: true);
            _ownedPunch = _ownedLabel.transform
                .DOPunchScale(Vector3.one * _punchScale, _punchDuration, 6, 0.4f)
                .SetUpdate(true);
        }

        private void PopFullMarker()
        {
            _fullPunch?.Kill(complete: true);
            _fullPunch = _fullMarker.transform
                .DOPunchScale(Vector3.one * _punchScale, _punchDuration, 6, 0.4f)
                .SetUpdate(true);
        }

        /// <summary>A collect or a purchase moves the bar visibly and tweens; a coin landing just sets it.</summary>
        private void RenderFill(float fill)
        {
            if (!_hasRendered)
            {
                _fillBar.fillAmount = fill;
                return;
            }

            if (Mathf.Abs(fill - _fillBar.fillAmount) < 0.02f && (_fillTween == null || !_fillTween.IsActive()))
            {
                _fillBar.fillAmount = fill;
                return;
            }

            _fillTween?.Kill();
            _fillTween = _fillBar.DOFillAmount(fill, _fillDuration).SetEase(Ease.OutQuad).SetUpdate(true);
        }
    }
}
