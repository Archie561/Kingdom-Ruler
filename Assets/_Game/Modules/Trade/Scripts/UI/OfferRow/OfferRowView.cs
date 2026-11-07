using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace KingdomRuler.Modules.Trade.UI
{
    /// <summary>
    /// One offer in the list: up to three resources given and three received, and a tap that opens
    /// its confirmation. Never marked or disabled for affordability — that is the confirmation's job.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class OfferRowView : MonoBehaviour
    {
        [Tooltip("Covers the whole row, over a raycast-target background, so a tap anywhere opens the offer.")]
        [SerializeField] private Button _button;

        [Tooltip("Three slots each: an offer moves 2–3 resources a side.")]
        [SerializeField] private ResourceAmountView[] _give;
        [SerializeField] private ResourceAmountView[] _receive;

        [Header("Animation")]
        [SerializeField] private float _arrivalDuration = 0.25f;
        [SerializeField] private float _removalDuration = 0.2f;

        private CanvasGroup _group;
        private Sequence    _tween;

        /// <summary>The offer shown, or null while the row is pooled.</summary>
        public string OfferId { get; private set; }

        public event Action<string> Pressed;

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            _button.onClick.AddListener(() => { if (OfferId != null) Pressed?.Invoke(OfferId); });
        }

        // A tab switch mid-tween must not leave a row half-faded.
        private void OnDisable() => ResetLook();

        private void OnDestroy() => _tween?.Kill();

        public void Show(OfferRowViewData data)
        {
            OfferId = data.OfferId;
            ResourceAmountView.ShowAll(_give, data.Give);
            ResourceAmountView.ShowAll(_receive, data.Receive);
            gameObject.SetActive(true);
        }

        /// <summary>Back to the pool: hidden and inert, so a tap on it can never open an old offer.</summary>
        public void Release()
        {
            OfferId = null;
            gameObject.SetActive(false);
        }

        public void PlayArrival(float delay)
        {
            ResetLook();
            _group.alpha = 0f;
            transform.localScale = Vector3.one * 0.92f;

            _tween = DOTween.Sequence()
                .AppendInterval(delay)
                .Append(_group.DOFade(1f, _arrivalDuration))
                .Join(transform.DOScale(1f, _arrivalDuration).SetEase(Ease.OutBack))
                .SetUpdate(true);
        }

        /// <summary>Fade out, then <see cref="Release"/>. Taps are ignored meanwhile.</summary>
        public void PlayRemoval()
        {
            ResetLook();
            OfferId = null;
            _group.blocksRaycasts = false;

            _tween = DOTween.Sequence()
                .Append(_group.DOFade(0f, _removalDuration))
                .Join(transform.DOScale(0.9f, _removalDuration).SetEase(Ease.InQuad))
                .OnComplete(Release)
                .SetUpdate(true);
        }

        private void ResetLook()
        {
            _tween?.Kill();
            if (_group == null) return;
            _group.alpha          = 1f;
            _group.blocksRaycasts = true;
            transform.localScale  = Vector3.one;
        }
    }
}
