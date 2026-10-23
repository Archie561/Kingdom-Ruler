using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace KingdomRuler.Modules.Laws.UI
{
    /// <summary>
    /// The law card: its text, the drag-to-swipe gesture and its animations.
    /// </summary>
    /// <remarks>
    /// <see cref="Swiped"/> fires the moment the drag crosses the threshold, before any animation:
    /// the decision is applied immediately and the animation is pure feel. While the swiped card
    /// flies off, <see cref="Show"/> and <see cref="Hide"/> are held back and applied once it has
    /// left the screen, so the departing card never changes its text mid-flight.
    /// </remarks>
    public sealed class LawCardView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Header("Content")]
        [SerializeField] private TextMeshProUGUI _titleLabel;
        [SerializeField] private TextMeshProUGUI _flavorLabel;

        [Header("Decision stamps")]
        [SerializeField] private CanvasGroup _acceptIndicator;
        [SerializeField] private CanvasGroup _rejectIndicator;

        // Distances are in canvas units; pointer deltas are converted in ToCanvasUnits.
        [Header("Swipe")]
        [Tooltip("Fraction of the card's width the player must drag to commit a swipe.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float _swipeThresholdFraction = 0.35f;

        [Tooltip("Degrees of rotation per 100 canvas units of horizontal drag.")]
        [SerializeField] private float _rotationPerHundredUnits = 10f;

        [Tooltip("Vertical lift (canvas units) at full horizontal displacement.")]
        [SerializeField] private float _verticalLift = 30f;

        [Header("Animation durations (seconds)")]
        [SerializeField] private float _springBackDuration  = 0.35f;
        [SerializeField] private float _swipeOffDuration    = 0.3f;
        [SerializeField] private float _arriveScaleDuration = 0.25f;

        private RectTransform _rectTransform;
        private Canvas        _canvas;
        private Vector2       _restPosition;
        private Vector2       _dragOffset;
        private bool          _isDragging;
        private bool          _isSwipingOff;

        // What the card should show once any swipe-off has finished.
        private bool            _hasCard;
        private LawCardViewData _card;

        private Sequence _swipeOffSequence;
        private Sequence _springBackSequence;
        private Tween    _scaleTween;

        /// <summary>Raised the moment a swipe is committed, before any animation.</summary>
        public event Action<LawDecision> Swiped;

        private float SwipeThreshold => Mathf.Max(1f, _rectTransform.rect.width * _swipeThresholdFraction);

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            _canvas        = GetComponentInParent<Canvas>();
            _restPosition  = _rectTransform.anchoredPosition;
            ClearIndicators();
        }

        // The card can be hidden mid-swipe (a tab switch); never come back believing one is in flight.
        private void OnDisable()
        {
            KillAllTweens();
            ResetToRest();
        }

        private void OnDestroy() => KillAllTweens();

        // ── Called by LawsScreenView ──────────────────────────────────────────────

        public void Show(LawCardViewData card)
        {
            _card    = card;
            _hasCard = true;
            if (!_isSwipingOff) ApplyContent();
        }

        public void Hide()
        {
            _hasCard = false;
            if (!_isSwipingOff) ApplyContent();
        }

        /// <summary>Scale the card in. Skipped mid-swipe: the reveal after the swipe animates it in anyway.</summary>
        public void PlayArrival()
        {
            if (!_isSwipingOff) ScaleIn();
        }

        // ── Drag gesture ──────────────────────────────────────────────────────────

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_isSwipingOff) return;

            _springBackSequence?.Kill();
            _scaleTween?.Kill();
            _dragOffset = Vector2.zero;
            _isDragging = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_isDragging) return;

            _dragOffset += ToCanvasUnits(eventData.delta);
            float t = _dragOffset.x / SwipeThreshold;

            _rectTransform.anchoredPosition = _restPosition + new Vector2(_dragOffset.x, Mathf.Abs(t) * _verticalLift);
            _rectTransform.localRotation    = Quaternion.Euler(0f, 0f, -_dragOffset.x / 100f * _rotationPerHundredUnits);

            if (_acceptIndicator != null) _acceptIndicator.alpha = Mathf.Clamp01(t);
            if (_rejectIndicator != null) _rejectIndicator.alpha = Mathf.Clamp01(-t);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_isDragging) return;
            _isDragging = false;

            float t = _dragOffset.x / SwipeThreshold;
            if (Mathf.Abs(t) >= 1f)
                CommitSwipe(t > 0f ? LawDecision.Accept : LawDecision.Reject);
            else
                SpringBack();
        }

        // anchoredPosition is in canvas units; with a scaling CanvasScaler, raw pixels would make
        // the card lag or lead the finger.
        private Vector2 ToCanvasUnits(Vector2 screenDelta)
        {
            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            return scale > 0f ? screenDelta / scale : screenDelta;
        }

        // ── Private ───────────────────────────────────────────────────────────────

        private void CommitSwipe(LawDecision decision)
        {
            // Set before raising Swiped: the decision re-renders the screen synchronously, and
            // that refresh must be held back until this card has left.
            _isSwipingOff = true;
            try
            {
                Swiped?.Invoke(decision);
            }
            finally
            {
                // Even if handling the decision throws, the card must finish its swipe and reset,
                // or it would refuse every later drag.
                PlaySwipeOff(decision);
            }
        }

        private void PlaySwipeOff(LawDecision decision)
        {
            float canvasHalfWidth = _canvas != null
                ? ((RectTransform)_canvas.transform).rect.width * 0.5f
                : _rectTransform.rect.width;
            float distance = canvasHalfWidth + _rectTransform.rect.width;
            float targetX  = _restPosition.x + (decision == LawDecision.Accept ? distance : -distance);

            _swipeOffSequence?.Kill();
            _swipeOffSequence = DOTween.Sequence()
                .Append(_rectTransform.DOAnchorPosX(targetX, _swipeOffDuration).SetEase(Ease.InBack))
                .SetUpdate(true)
                // OnComplete rather than AppendCallback, so Kill() skips the reveal.
                .OnComplete(FinishSwipeOff);
        }

        private void FinishSwipeOff()
        {
            _swipeOffSequence = null;
            ResetToRest();
            ApplyContent();
            if (_hasCard) ScaleIn();
        }

        private void ApplyContent()
        {
            if (_hasCard)
            {
                _titleLabel.SetText(_card.Title);
                _flavorLabel.SetText(_card.FlavorText);
            }
            gameObject.SetActive(_hasCard);
        }

        private void ScaleIn()
        {
            _scaleTween?.Kill();
            _rectTransform.localScale = Vector3.zero;
            _scaleTween = _rectTransform.DOScale(Vector3.one, _arriveScaleDuration).SetEase(Ease.OutBack).SetUpdate(true);
        }

        private void SpringBack()
        {
            _springBackSequence?.Kill();
            _springBackSequence = DOTween.Sequence()
                .Append(_rectTransform.DOAnchorPos(_restPosition, _springBackDuration).SetEase(Ease.OutElastic))
                .Join(_rectTransform.DOLocalRotate(Vector3.zero, _springBackDuration).SetEase(Ease.OutElastic))
                .SetUpdate(true)
                .OnComplete(ClearIndicators);
        }

        private void ResetToRest()
        {
            _isSwipingOff                   = false;
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
