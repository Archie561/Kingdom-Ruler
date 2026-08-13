using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using KingdomRuler.Modules.Laws.Presenters;
using KingdomRuler.Shared.Ledger;

namespace KingdomRuler.Modules.Laws.Views
{
    /// <summary>
    /// One characteristic row: name, level number, progress bar, and buy-up button.
    /// Renders a <see cref="CharacteristicDisplayData"/> handed to it by LawsView.
    /// DOTween animations live here, never in the Presenter or Manager.
    /// </summary>
    public sealed class CharacteristicBarView : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("The characteristic this bar represents. Set in the Inspector for each bar instance.")]
        [SerializeField] private CharacteristicType _type;

        [Header("Display")]
        [SerializeField] private Image           _icon;
        [SerializeField] private TextMeshProUGUI _nameLabel;
        [SerializeField] private TextMeshProUGUI _levelLabel;
        [SerializeField] private Image           _fillBar;

        [Header("Buy-Up")]
        [SerializeField] private Button          _buyUpButton;
        [SerializeField] private TextMeshProUGUI _buyUpCostLabel;

        // ── Animation tuning ─────────────────────────────────────────────────────
        [Header("Animation")]
        [Tooltip("How long the bar takes to slide to a new value within the same level.")]
        [SerializeField] private float _barFillDuration = 0.4f;

        [Tooltip("How long the bar takes to top off to full when a level is completed.")]
        [SerializeField] private float _levelUpTopOffDuration = 0.18f;

        [SerializeField] private float _levelUpPunchScale    = 0.5f;
        [SerializeField] private float _levelUpPunchDuration = 0.5f;

        private Tween _fillTween;
        private Tween _punchTween;

        // Last rendered values, so a refresh triggered by something unrelated doesn't
        // restart six bar tweens and rewrite six labels that did not change.
        private bool   _hasRendered;
        private string _renderedName;
        private Sprite _renderedIcon;
        private int    _renderedLevel;
        private float _renderedProgress;
        private int   _renderedBuyUpCost;
        private bool  _renderedCanAfford;

        /// <summary>Which characteristic this bar is bound to.</summary>
        public CharacteristicType Type => _type;

        /// <summary>Raised when the player taps this row's buy-up button.</summary>
        public event Action<CharacteristicType> OnBuyUpPressed;

        private void Awake()
        {
            _buyUpButton.onClick.AddListener(HandleBuyUpClicked);
        }

        private void OnDestroy()
        {
            _buyUpButton.onClick.RemoveListener(HandleBuyUpClicked);
            _fillTween?.Kill();
            _punchTween?.Kill();
        }

        private void HandleBuyUpClicked() => OnBuyUpPressed?.Invoke(_type);

        // ── Public API called by LawsView ─────────────────────────────────────────

        private void SetNameLabel(string displayName)
        {
            if (_nameLabel != null) _nameLabel.SetText(displayName);
        }

        /// <summary>
        /// Show the characteristic's icon, or hide the slot entirely when there isn't one.
        /// </summary>
        /// <remarks>
        /// Disabling the Image rather than leaving it with a null sprite matters: a UGUI
        /// Image with no sprite still draws a filled white rectangle, which reads as a
        /// missing-art bug rather than as a row that simply has no icon yet.
        /// </remarks>
        private void SetIcon(Sprite icon)
        {
            if (_icon == null) return;

            _icon.sprite = icon;
            _icon.enabled = icon != null;
        }

        /// <summary>Render new display data, animating only what actually changed.</summary>
        public void UpdateDisplay(CharacteristicDisplayData data)
        {
            bool firstRender = !_hasRendered;
            bool leveledUp   = !firstRender && data.Level > _renderedLevel;

            // The name is localized, so it changes with the locale, not just on first render.
            if (firstRender || data.Name != _renderedName)
                SetNameLabel(data.Name);

            if (firstRender || data.Icon != _renderedIcon)
                SetIcon(data.Icon);

            if (firstRender || data.Level != _renderedLevel)
                _levelLabel.SetText(data.Level.ToString());

            if (firstRender)
                SetFillImmediate(data.ProgressFraction);
            else if (leveledUp)
                PlayLevelUpFill(data.ProgressFraction);
            else if (!Mathf.Approximately(data.ProgressFraction, _renderedProgress))
                TweenFillTo(data.ProgressFraction);

            if (firstRender || data.CanAffordBuyUp != _renderedCanAfford)
                _buyUpButton.interactable = data.CanAffordBuyUp;

            if (firstRender || data.BuyUpCost != _renderedBuyUpCost)
                _buyUpCostLabel.SetText(data.BuyUpCost.ToString());

            _hasRendered       = true;
            _renderedName      = data.Name;
            _renderedIcon      = data.Icon;
            _renderedLevel     = data.Level;
            _renderedProgress  = data.ProgressFraction;
            _renderedBuyUpCost = data.BuyUpCost;
            _renderedCanAfford = data.CanAffordBuyUp;
        }

        /// <summary>Punch-scale celebration on the level number.</summary>
        public void PlayLevelUpCelebration()
        {
            _punchTween?.Kill(complete: true);
            _punchTween = _levelLabel.transform
                .DOPunchScale(Vector3.one * _levelUpPunchScale, _levelUpPunchDuration, 5, 0.1f)
                .SetUpdate(true);
        }

        // ── Fill animation ────────────────────────────────────────────────────────

        private void SetFillImmediate(float progress)
        {
            _fillTween?.Kill();
            _fillTween = null;
            _fillBar.fillAmount = progress;
        }

        private void TweenFillTo(float progress)
        {
            _fillTween?.Kill();
            _fillTween = _fillBar
                .DOFillAmount(progress, _barFillDuration)
                .SetEase(Ease.OutQuart)
                .SetUpdate(true);
        }

        /// <summary>
        /// Level-up fill: top off to full, snap back to empty, then fill the carried-over
        /// remainder.
        /// </summary>
        /// <remarks>
        /// Levelling resets progress, so the raw numbers go from nearly-full to nearly-empty.
        /// Tweening straight to the new value therefore ran the bar *backwards* through its
        /// whole length — reading as a loss at the exact moment the player earned something.
        /// The player has to see the bar complete before it resets.
        /// </remarks>
        private void PlayLevelUpFill(float progressAfterLevelUp)
        {
            _fillTween?.Kill();
            _fillTween = DOTween.Sequence()
                .Append(_fillBar.DOFillAmount(1f, _levelUpTopOffDuration).SetEase(Ease.OutQuad))
                .AppendCallback(() => _fillBar.fillAmount = 0f)
                .Append(_fillBar.DOFillAmount(progressAfterLevelUp, _barFillDuration).SetEase(Ease.OutQuart))
                // Unscaled, matching the punch that plays alongside it — on different
                // clocks the two would drift apart the moment anything touches timeScale.
                .SetUpdate(true);
        }
    }
}
