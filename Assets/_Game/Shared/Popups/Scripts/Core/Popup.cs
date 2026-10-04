using System;
using DG.Tweening;
using UnityEngine;

namespace KingdomRuler.Shared.Popups
{
    /// <summary>
    /// What every popup has in common, whatever it shows: it appears, closes and animates.
    /// </summary>
    /// <remarks>
    /// <para>A popup derives from <see cref="Popup{TData, TChoice}"/>, not from this class. This
    /// one exists because popups of different kinds have unrelated generic types, and
    /// <see cref="PopupManager"/> and <see cref="PopupRegistry"/> hold them side by side.</para>
    ///
    /// <para>A popup lives through three states: <b>hidden</b> (just created by
    /// <see cref="PopupManager.Create{T}"/>), <b>open</b> (from its <c>Show</c> or <c>Ask</c>)
    /// and <b>closed</b> (animating out, then destroyed).</para>
    ///
    /// <para>A popup owns its content, its animation and its lifetime — it destroys itself once
    /// it has animated out. Where it sits among the other popups, the backdrop and the sounds
    /// belong to the manager, which reacts to <see cref="Shown"/> and <see cref="Closed"/>.</para>
    /// </remarks>
    public abstract class Popup : MonoBehaviour
    {
        [Header("Panel")]
        [Tooltip("The part that scales in and out. Not the root, which fills the screen.")]
        [SerializeField] private RectTransform _panel;

        [Header("Animation")]
        [SerializeField] private float _openDuration   = 0.20f;
        [SerializeField] private float _closeDuration  = 0.13f;
        [SerializeField] private float _collapsedScale = 0.85f;

        private bool _isShown;
        private bool _isClosed;

        /// <summary>
        /// True while the player can see and use the popup: after <c>Show</c> / <c>Ask</c>, until
        /// it closes. False for a popup that was created but not shown yet.
        /// </summary>
        public bool IsOpen => _isShown && !_isClosed;

        /// <summary>True from the moment the popup starts closing. A closed popup ignores its buttons.</summary>
        protected bool IsClosed => _isClosed;

        /// <summary>The popup has appeared on screen. Raised once.</summary>
        public event Action Shown;

        /// <summary>
        /// The popup has closed: it no longer takes input and is animating out. Raised once.
        /// </summary>
        public event Action Closed;

        /// <summary>
        /// Close without a choice. Safe to call any number of times — only the first one counts,
        /// so a double tap or a backdrop tap racing a button cannot close twice.
        /// </summary>
        public void Close()
        {
            bool wasOnScreen = _isShown;
            if (!MarkClosed()) return;

            // Never shown, so there is nothing to animate out.
            if (wasOnScreen) PlayCloseAnimationThenDestroy();
            else             Destroy(gameObject);
        }

        /// <summary>
        /// Let go of whoever is waiting on this popup. Called once, as the popup closes.
        /// </summary>
        protected abstract void ReleaseCaller();

        /// <summary>
        /// Put the hidden popup on screen and scale it in. Called once, by <c>Show</c>.
        /// </summary>
        protected void Reveal()
        {
            gameObject.SetActive(true);
            _isShown = true;

            // The manager stacks it, moves the backdrop behind it and plays the open sound.
            Shown?.Invoke();
            PlayOpenAnimation();
        }

        private void PlayOpenAnimation()
        {
            if (_panel == null) return;

            _panel.localScale = Vector3.one * _collapsedScale;

            // SetUpdate(true): animate even if the game is paused behind the popup.
            // SetLink: a popup destroyed mid-tween takes its tween with it.
            _panel.DOScale(1f, _openDuration)
                  .SetEase(Ease.OutBack)
                  .SetUpdate(true)
                  .SetLink(gameObject);
        }

        /// <remarks>
        /// Override only to add to it, and call the base: this is what keeps a destroyed popup
        /// from leaving its caller waiting forever.
        /// </remarks>
        protected virtual void OnDestroy()
        {
            // Destroyed without being closed — the app quitting, say. Close it now, so whoever is
            // waiting for a choice hears "none" instead of waiting forever.
            MarkClosed();
        }

        private bool MarkClosed()
        {
            if (_isClosed) return false;
            _isClosed = true;

            // The manager hears first, so its stack is already up to date when the caller
            // resumes — a caller that opens the next popup straight away gets it on top.
            Closed?.Invoke();
            ReleaseCaller();
            return true;
        }

        private void PlayCloseAnimationThenDestroy()
        {
            if (_panel == null)
            {
                Destroy(gameObject);
                return;
            }

            _panel.DOKill();   // an open animation still running
            _panel.DOScale(_collapsedScale, _closeDuration)
                  .SetEase(Ease.InQuad)
                  .SetUpdate(true)
                  .SetLink(gameObject)
                  .OnComplete(() => Destroy(gameObject));
        }
    }
}
