using System;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using DG.Tweening;
using KingdomRuler.Modules.Laws.Presenters;
using KingdomRuler.Shared.Ledger;

namespace KingdomRuler.Modules.Laws.Views
{
    /// <summary>
    /// One characteristic dial: a circular progress ring with the characteristic's icon in
    /// the middle and its level on a nameplate underneath.
    /// Renders a <see cref="CharacteristicDisplayData"/> handed to it by LawsView.
    /// DOTween animations live here, never in the Presenter or Manager.
    /// </summary>
    /// <remarks>
    /// <para>The dial shows <b>level</b>, not points. Exact point totals belong in the
    /// detail panel, so the six dials stay readable at a glance across the top of the
    /// screen.</para>
    ///
    /// <para>There is no name label: a characteristic is identified by its icon.
    /// <see cref="CharacteristicDisplayData.Name"/> is still resolved for the detail panel
    /// and for other modules, it is simply not drawn here.</para>
    ///
    /// <para>The ring is an <c>Image</c> with <c>type = Filled</c> and
    /// <c>fillMethod = Radial360</c>. Nothing in this class knows that — it sets
    /// <c>fillAmount</c>, which works identically for a bar or a ring, so the fill and
    /// level-up animations below were unchanged by the switch to a circular design.</para>
    /// </remarks>
    public sealed class CharacteristicBarView : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("The characteristic this dial represents. Set in the Inspector for each instance.")]
        [SerializeField] private CharacteristicType _type;

        [Header("Display")]
        [Tooltip("Characteristic icon, centred inside the ring.")]
        [SerializeField] private Image           _icon;

        [Tooltip("Level number on the nameplate below the ring.")]
        [SerializeField] private TextMeshProUGUI _levelLabel;

        [Tooltip("The progress ring itself: Image type Filled, fillMethod Radial360.")]
        [SerializeField] private Image           _fillBar;

        [Header("Interaction")]
        [Tooltip("Covers the whole dial. Will open the characteristic detail panel.")]
        // Renamed from _buyUpButton when buy-up moved off the dial. FormerlySerializedAs keeps
        // the prefab's existing reference — without it Unity matches by field name, silently
        // drops the link, and the button arrives null at runtime.
        [FormerlySerializedAs("_buyUpButton")]
        [SerializeField] private Button          _dialButton;

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
        private Sprite _renderedIcon;
        private int    _renderedLevel;
        private float  _renderedProgress;

        /// <summary>Which characteristic this bar is bound to.</summary>
        public CharacteristicType Type => _type;

        /// <summary>
        /// Raised when the player taps this dial. The dial reports the input; what it means is
        /// LawsView's decision — currently nothing, until the detail panel exists.
        /// </summary>
        public event Action<CharacteristicType> OnDialPressed;

        private void Awake()
        {
            _dialButton.onClick.AddListener(HandleDialClicked);
        }

        private void OnDestroy()
        {
            _dialButton.onClick.RemoveListener(HandleDialClicked);
            _fillTween?.Kill();
            _punchTween?.Kill();
        }

        private void HandleDialClicked() => OnDialPressed?.Invoke(_type);

        // ── Public API called by LawsView ─────────────────────────────────────────

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

            // Nothing here reads BuyUpCost or CanAffordBuyUp any more: the dial shows level
            // only, and the purchase lives in the detail panel. Both fields stay on the
            // display struct because that panel is what will render them.

            _hasRendered      = true;
            _renderedIcon     = data.Icon;
            _renderedLevel    = data.Level;
            _renderedProgress = data.ProgressFraction;
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
