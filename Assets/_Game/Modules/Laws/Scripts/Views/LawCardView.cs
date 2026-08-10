using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DG.Tweening;
using KingdomRuler.Modules.Laws.Presenters;

namespace KingdomRuler.Modules.Laws.Views
{
    /// <summary>
    /// Displays a single law card and handles the Tinder-style drag-to-swipe gesture.
    /// No physics — uses UI drag events + DOTween spring-back / swipe-off animation.
    ///
    /// Architecture contract — business logic is NEVER gated on animation:
    ///   OnSwiped        — fired IMMEDIATELY when the drag threshold is crossed.
    ///                     LawsView forwards this to the Presenter right away.
    ///   OnSwipeAnimationComplete — fired when the card has finished flying off screen.
    ///                             LawsView uses this to visually reveal the next card.
    ///
    /// This means: card resolution, Ledger mutations, and state-change notifications
    /// all happen before any DOTween sequence runs. The animations are purely cosmetic.
    /// </summary>
    public sealed class LawCardView : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        // ── Sub-element references ────────────────────────────────────────────────
        [Header("Card Content")]
        [SerializeField] private TextMeshProUGUI _titleLabel;
        [SerializeField] private TextMeshProUGUI _flavorLabel;
        [SerializeField] private TextMeshProUGUI _acceptEffectsSummary;
        [SerializeField] private TextMeshProUGUI _rejectEffectsSummary;

        [Header("Visual Feedback")]
        [SerializeField] private CanvasGroup _acceptIndicator;  // "✓ Accept" stamp
        [SerializeField] private CanvasGroup _rejectIndicator;  // "✗ Reject" stamp

        // ── Swipe tuning ──────────────────────────────────────────────────────────
        // All distances below are in CANVAS units, never screen pixels. Pointer deltas
        // arrive in screen pixels and are converted once, in ToCanvasUnits().
        [Header("Swipe Behaviour")]
        [Tooltip("Fraction of the card's own width the player must drag to commit a swipe. " +
                 "Expressed as a fraction so the gesture feels identical on every screen density.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float _swipeThresholdFraction = 0.35f;

        [Tooltip("How many degrees the card rotates per 100 canvas units of horizontal drag.")]
        [SerializeField] private float _rotationPerHundredUnits = 10f;

        [Tooltip("Vertical lift (canvas units) the card gains at full horizontal displacement.")]
        [SerializeField] private float _verticalLift = 30f;

        [Header("Animation Durations (seconds)")]
        [SerializeField] private float _springBackDuration  = 0.35f;
        [SerializeField] private float _swipeOffDuration    = 0.3f;
        [SerializeField] private float _arriveScaleDuration = 0.25f;

        // ── State ─────────────────────────────────────────────────────────────────
        private RectTransform _rectTransform;
        private Canvas        _canvas;
        private Vector2       _restPosition;
        private Vector2       _dragOffset;
        private bool          _isDragging;

        // Held so they can be killed deterministically. Killing via DOTween.Kill(target)
        // does not reach an untargeted Sequence, which is how the swipe-off callback used
        // to be lost — leaving IsSwipeAnimating stuck true forever.
        private Sequence _swipeOffSequence;
        private Sequence _springBackSequence;
        private Tween    _scaleTween;

        /// <summary>Drag distance in canvas units required to commit a swipe.</summary>
        private float SwipeThreshold => Mathf.Max(1f, _rectTransform.rect.width * _swipeThresholdFraction);

        // ── Events ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Raised IMMEDIATELY when the drag threshold is crossed — before any animation.
        /// <c>true</c> = accepted (right), <c>false</c> = rejected (left).
        /// LawsView must forward this to the Presenter without waiting for the animation.
        /// </summary>
        public event Action<bool> OnSwiped;

        /// <summary>
        /// Raised after the swipe-off animation completes and the card has reset to its
        /// rest position. LawsView uses this as the cue to populate and reveal the next card.
        /// This is a COSMETIC-ONLY callback — it must never gate business logic.
        /// </summary>
        public event Action OnSwipeAnimationComplete;

        /// <summary>True while a swipe-off animation is playing.</summary>
        public bool IsSwipeAnimating { get; private set; }

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            _canvas        = GetComponentInParent<Canvas>();
            _restPosition  = _rectTransform.anchoredPosition;

            if (_acceptIndicator != null) _acceptIndicator.alpha = 0f;
            if (_rejectIndicator != null) _rejectIndicator.alpha = 0f;
        }

        /// <summary>
        /// The card can be hidden mid-swipe (LawsView hides it when there's no active
        /// card). Tear the animation down and reset here so the object is never
        /// re-enabled still believing a swipe is in flight.
        /// </summary>
        private void OnDisable()
        {
            KillAllTweens();
            ResetToRest();
        }

        private void OnDestroy()
        {
            KillAllTweens();
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Populate the card's text fields from render-ready data.
        /// Safe to call at any time, including while a swipe animation is playing
        /// (Populate only updates text — it never touches DOTween or position).
        /// </summary>
        public void Populate(LawCardDisplayData data)
        {
            _titleLabel.SetText(data.TitleKey);
            _flavorLabel.SetText(data.FlavorTextKey);
            _acceptEffectsSummary.SetText(data.AcceptSummary);
            _rejectEffectsSummary.SetText(data.RejectSummary);
        }

        /// <summary>Scale-in animation when a new card enters the play area.</summary>
        public void AnimateIn()
        {
            _scaleTween?.Kill();
            _rectTransform.localScale = Vector3.zero;
            _scaleTween = _rectTransform.DOScale(Vector3.one, _arriveScaleDuration).SetEase(Ease.OutBack);
        }

        // ── Drag gesture ───────────────────────────────────────────────────────────

        public void OnBeginDrag(PointerEventData eventData)
        {
            // Refuse to start a new drag while the previous card is still flying off.
            // Interrupting that sequence would strand IsSwipeAnimating at true and the
            // "reveal the next card" callback would never fire.
            if (IsSwipeAnimating) return;

            _springBackSequence?.Kill();
            _springBackSequence = null;
            _scaleTween?.Kill();
            _scaleTween = null;

            _dragOffset = Vector2.zero;
            _isDragging = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_isDragging) return;

            _dragOffset += ToCanvasUnits(eventData.delta);

            float t = _dragOffset.x / SwipeThreshold;

            float liftY = Mathf.Abs(t) * _verticalLift;
            _rectTransform.anchoredPosition = _restPosition + new Vector2(_dragOffset.x, liftY);

            float angle = -(_dragOffset.x / 100f) * _rotationPerHundredUnits;
            _rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);

            if (_acceptIndicator != null) _acceptIndicator.alpha = Mathf.Clamp01(t);
            if (_rejectIndicator != null) _rejectIndicator.alpha = Mathf.Clamp01(-t);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_isDragging) return;
            _isDragging = false;

            float normalised = _dragOffset.x / SwipeThreshold;
            if (Mathf.Abs(normalised) >= 1f)
                CommitSwipe(normalised > 0f);
            else
                SpringBack();
        }

        /// <summary>
        /// Convert a screen-pixel pointer delta into canvas units.
        /// <c>anchoredPosition</c> is in canvas units, so with a CanvasScaler set to
        /// Scale-With-Screen-Size the two diverge on every device that isn't at the
        /// reference resolution — the card would lag or lead the finger.
        /// </summary>
        private Vector2 ToCanvasUnits(Vector2 screenDelta)
        {
            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            return scale > 0f ? screenDelta / scale : screenDelta;
        }

        // ── Private helpers ────────────────────────────────────────────────────────

        private void CommitSwipe(bool accepted)
        {
            // ── Business logic fires IMMEDIATELY ──────────────────────────────────
            // Presenter processes card resolution, Ledger updates, and state-change
            // notifications before any animation frame runs. This is non-negotiable.
            OnSwiped?.Invoke(accepted);

            // ── Animation runs INDEPENDENTLY as pure feel ─────────────────────────
            // The card content may already be updated by Populate() by the time this
            // tween completes — that's fine; it's off-screen by then.
            PlaySwipeOffAnimation(accepted);
        }

        private void PlaySwipeOffAnimation(bool accepted)
        {
            // Travel far enough that the card clears the screen regardless of resolution,
            // expressed in canvas units to match anchoredPosition.
            float canvasHalfWidth = _canvas != null
                ? ((RectTransform)_canvas.transform).rect.width * 0.5f
                : _rectTransform.rect.width;
            float distance = canvasHalfWidth + _rectTransform.rect.width;
            float targetX  = _restPosition.x + (accepted ? distance : -distance);

            IsSwipeAnimating = true;

            _swipeOffSequence?.Kill();
            _swipeOffSequence = DOTween.Sequence()
                .Append(_rectTransform
                    .DOAnchorPosX(targetX, _swipeOffDuration)
                    .SetEase(Ease.InBack))
                // OnComplete (not AppendCallback) so a Kill() with complete:false skips it
                // rather than firing the "next card" cue for an animation that was cancelled.
                .OnComplete(OnSwipeAnimationFinished);
        }

        private void OnSwipeAnimationFinished()
        {
            _swipeOffSequence = null;
            ResetToRest();

            // Cosmetic-only signal — LawsView uses this to reveal the next card.
            OnSwipeAnimationComplete?.Invoke();
        }

        private void SpringBack()
        {
            _springBackSequence?.Kill();
            _springBackSequence = DOTween.Sequence()
                .Append(_rectTransform.DOAnchorPos(_restPosition, _springBackDuration).SetEase(Ease.OutElastic))
                .Join(_rectTransform.DOLocalRotate(Vector3.zero, _springBackDuration).SetEase(Ease.OutElastic))
                .OnComplete(() =>
                {
                    _springBackSequence = null;
                    ClearIndicators();
                });
        }

        /// <summary>
        /// Single place that returns the card to a clean, interactable state.
        /// Clearing <see cref="IsSwipeAnimating"/> here — rather than only on the
        /// animation's completion callback — is what guarantees the card can never
        /// get stranded mid-swipe and block every future refresh.
        /// </summary>
        private void ResetToRest()
        {
            IsSwipeAnimating                = false;
            _isDragging                     = false;
            _dragOffset                     = Vector2.zero;
            _rectTransform.anchoredPosition = _restPosition;
            _rectTransform.localRotation    = Quaternion.identity;
            _rectTransform.localScale       = Vector3.one;
            ClearIndicators();
        }

        private void ClearIndicators()
        {
            if (_acceptIndicator != null) _acceptIndicator.alpha = 0f;
            if (_rejectIndicator != null) _rejectIndicator.alpha = 0f;
        }

        private void KillAllTweens()
        {
            _swipeOffSequence?.Kill();
            _springBackSequence?.Kill();
            _scaleTween?.Kill();
            _swipeOffSequence   = null;
            _springBackSequence = null;
            _scaleTween         = null;
        }

    }
}
