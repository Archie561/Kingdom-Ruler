using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Trade.UI
{
    /// <summary>
    /// One resource on the Trade screen: icon, name, level, stock / capacity and a fill bar. Always
    /// visible, because every offer is judged against these numbers. A tap anywhere on it opens that
    /// resource's warehouse popup.
    /// </summary>
    public sealed class ResourceTileView : MonoBehaviour
    {
        [Tooltip("The resource this tile shows. Set per instance.")]
        [SerializeField] private TradeResourceType _type;

        [Tooltip("Covers the whole tile, over a raycast-target background.")]
        [SerializeField] private Button _button;

        [Header("Display")]
        [SerializeField] private Image           _icon;
        [SerializeField] private TextMeshProUGUI _nameLabel;
        [SerializeField] private TextMeshProUGUI _levelLabel;
        [SerializeField] private TextMeshProUGUI _stockLabel;
        [Tooltip("Image type Filled, fillMethod Horizontal.")]
        [SerializeField] private Image           _fillBar;

        [Header("Animation")]
        [SerializeField] private float _fillDuration  = 0.3f;
        [SerializeField] private float _punchScale    = 0.25f;
        [SerializeField] private float _punchDuration = 0.4f;

        private Tween _fillTween;
        private Tween _punchTween;

        // Last values drawn, so steady polling rewrites nothing and dirties nothing.
        private bool   _hasRendered;
        private int    _shownStock, _shownCapacity;
        private Sprite _shownIcon;

        public TradeResourceType Type => _type;

        /// <summary>The level whose text is on the label; 0 before any. The screen view refreshes the text when it differs.</summary>
        public int ShownLevel { get; private set; }

        public event Action<TradeResourceType> Pressed;

        private void Awake() => _button.onClick.AddListener(() => Pressed?.Invoke(_type));

        private void OnDestroy()
        {
            _fillTween?.Kill();
            _punchTween?.Kill();
        }

        /// <summary>Draw the numbers, rewriting only what changed.</summary>
        public void Render(ResourceTileViewData data)
        {
            if (!_hasRendered || data.Icon != _shownIcon)
            {
                // An Image without a sprite draws a white box, so hide it instead.
                _icon.sprite  = data.Icon;
                _icon.enabled = data.Icon != null;
                _shownIcon    = data.Icon;
            }

            if (_hasRendered && data.Stock == _shownStock && data.Capacity == _shownCapacity) return;

            _stockLabel.SetText($"{data.Stock:N0} / {data.Capacity:N0}");

            _fillTween?.Kill();
            if (_hasRendered)
                _fillTween = _fillBar.DOFillAmount(data.Fill, _fillDuration).SetEase(Ease.OutQuad).SetUpdate(true);
            else
                _fillBar.fillAmount = data.Fill;

            _hasRendered   = true;
            _shownStock    = data.Stock;
            _shownCapacity = data.Capacity;
        }

        public void SetName(string name) => _nameLabel.SetText(name);

        public void SetLevel(int displayedLevel, string text)
        {
            ShownLevel = displayedLevel;
            _levelLabel.SetText(text);
        }

        /// <summary>The warehouse was upgraded.</summary>
        public void PlayUpgrade()
        {
            _punchTween?.Kill(complete: true);
            _punchTween = _levelLabel.transform
                .DOPunchScale(Vector3.one * _punchScale, _punchDuration, 6, 0.4f)
                .SetUpdate(true);
        }
    }
}
