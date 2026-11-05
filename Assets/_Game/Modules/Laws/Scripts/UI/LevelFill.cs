using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace KingdomRuler.Modules.Laws.UI
{
    /// <summary>
    /// A level-progress fill — the dial's ring and the characteristics popup's bars. Tweens to each
    /// new value; on a level-up it fills to full, resets, then fills the carried-over progress, so the
    /// fill never runs backwards at the moment the player earned something.
    /// </summary>
    /// <remarks>Shape is the Image's configuration (radial ring, horizontal bar); this only sets <c>fillAmount</c>.</remarks>
    public sealed class LevelFill
    {
        private readonly Image _image;
        private readonly float _fillDuration;
        private readonly float _topOffDuration;

        private Tween _tween;
        private bool  _hasRendered;
        private int   _renderedLevel;
        private float _renderedProgress;

        public LevelFill(Image image, float fillDuration, float topOffDuration)
        {
            _image          = image;
            _fillDuration   = fillDuration;
            _topOffDuration = topOffDuration;
        }

        /// <summary>Show <paramref name="progress"/> (0..1) within <paramref name="level"/>, animating only what changed.</summary>
        public void Render(int level, float progress)
        {
            if (!_hasRendered)
                SetImmediate(progress);
            else if (level > _renderedLevel)
                PlayLevelUp(progress);
            else if (!Mathf.Approximately(progress, _renderedProgress))
                TweenTo(progress);

            _hasRendered      = true;
            _renderedLevel    = level;
            _renderedProgress = progress;
        }

        public void Kill() => _tween?.Kill();

        private void SetImmediate(float progress)
        {
            _tween?.Kill();
            _image.fillAmount = progress;
        }

        private void TweenTo(float progress)
        {
            _tween?.Kill();
            _tween = _image.DOFillAmount(progress, _fillDuration).SetEase(Ease.OutQuart).SetUpdate(true);
        }

        private void PlayLevelUp(float progressAfterLevelUp)
        {
            _tween?.Kill();
            _tween = DOTween.Sequence()
                .Append(_image.DOFillAmount(1f, _topOffDuration).SetEase(Ease.OutQuad))
                .AppendCallback(() => _image.fillAmount = 0f)
                .Append(_image.DOFillAmount(progressAfterLevelUp, _fillDuration).SetEase(Ease.OutQuart))
                .SetUpdate(true);
        }
    }
}
