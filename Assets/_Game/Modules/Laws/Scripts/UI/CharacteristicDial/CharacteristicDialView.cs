using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Laws.UI
{
    /// <summary>
    /// One characteristic dial: a progress ring, the icon inside it and the level below.
    /// </summary>
    /// <remarks>
    /// The ring's shape is prefab configuration (an Image with <c>fillMethod = Radial360</c>);
    /// this class only sets <c>fillAmount</c>.
    /// </remarks>
    public sealed class CharacteristicDialView : MonoBehaviour
    {
        [Tooltip("The characteristic this dial shows. Set per instance.")]
        [SerializeField] private CharacteristicType _type;

        [Header("Display")]
        [SerializeField] private Image           _icon;
        [SerializeField] private TextMeshProUGUI _levelLabel;
        [Tooltip("The progress ring: Image type Filled, fillMethod Radial360.")]
        [SerializeField] private Image           _fillBar;
        [Tooltip("Covers the whole dial.")]
        [SerializeField] private Button          _dialButton;

        [Header("Animation")]
        [Tooltip("Seconds to slide the ring to a new value within a level.")]
        [SerializeField] private float _barFillDuration = 0.4f;
        [Tooltip("Seconds to top the ring off when a level completes.")]
        [SerializeField] private float _levelUpTopOffDuration = 0.18f;
        [SerializeField] private float _levelUpPunchScale     = 0.5f;
        [SerializeField] private float _levelUpPunchDuration  = 0.5f;

        private Tween _fillTween;
        private Tween _punchTween;

        // Last rendered values, so an unrelated refresh does not restart the ring tween.
        private bool   _hasRendered;
        private Sprite _renderedIcon;
        private int    _renderedLevel;
        private float  _renderedProgress;

        public CharacteristicType Type => _type;

        /// <summary>
        /// Raised when the dial is tapped. Unsubscribed for now: its first listener is the
        /// characteristic detail panel, which is not built yet.
        /// </summary>
        public event Action<CharacteristicType> Pressed;

        private void Awake() => _dialButton.onClick.AddListener(() => Pressed?.Invoke(_type));

        private void OnDestroy()
        {
            _fillTween?.Kill();
            _punchTween?.Kill();
        }

        public void Render(CharacteristicDialViewData data)
        {
            bool firstRender = !_hasRendered;

            if (firstRender || data.Icon != _renderedIcon)
            {
                // An Image without a sprite draws a white box, so hide it instead.
                _icon.sprite  = data.Icon;
                _icon.enabled = data.Icon != null;
            }

            if (firstRender || data.Level != _renderedLevel)
                _levelLabel.SetText(data.Level.ToString());

            if (firstRender)
                SetFillImmediate(data.Progress);
            else if (data.Level > _renderedLevel)
                PlayLevelUpFill(data.Progress);
            else if (!Mathf.Approximately(data.Progress, _renderedProgress))
                TweenFillTo(data.Progress);

            _hasRendered      = true;
            _renderedIcon     = data.Icon;
            _renderedLevel    = data.Level;
            _renderedProgress = data.Progress;
        }

        public void PlayLevelUp()
        {
            _punchTween?.Kill(complete: true);
            _punchTween = _levelLabel.transform
                .DOPunchScale(Vector3.one * _levelUpPunchScale, _levelUpPunchDuration, 5, 0.1f)
                .SetUpdate(true);
        }

        private void SetFillImmediate(float progress)
        {
            _fillTween?.Kill();
            _fillBar.fillAmount = progress;
        }

        private void TweenFillTo(float progress)
        {
            _fillTween?.Kill();
            _fillTween = _fillBar.DOFillAmount(progress, _barFillDuration).SetEase(Ease.OutQuart).SetUpdate(true);
        }

        /// <summary>
        /// Fill to full, reset, then fill the carried-over progress — so a level-up never reads as
        /// the ring running backwards.
        /// </summary>
        private void PlayLevelUpFill(float progressAfterLevelUp)
        {
            _fillTween?.Kill();
            _fillTween = DOTween.Sequence()
                .Append(_fillBar.DOFillAmount(1f, _levelUpTopOffDuration).SetEase(Ease.OutQuad))
                .AppendCallback(() => _fillBar.fillAmount = 0f)
                .Append(_fillBar.DOFillAmount(progressAfterLevelUp, _barFillDuration).SetEase(Ease.OutQuart))
                .SetUpdate(true);
        }
    }
}
