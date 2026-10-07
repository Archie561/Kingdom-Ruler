using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using VContainer.Unity;
using KingdomRuler.Systems.Audio;

namespace KingdomRuler.Systems.Popups
{
    /// <summary>
    /// Creates popups and keeps the shown ones in order: the newest on top, one backdrop directly
    /// behind it.
    /// </summary>
    /// <remarks>
    /// <para>Every mechanic opens its popups through this one object, so stacking, dimming and
    /// the open/close sounds are written once.</para>
    ///
    /// <para>A MonoBehaviour because it owns the popup canvas. It lives in the Bootstrap scene
    /// so anything built at startup can be given it.</para>
    /// </remarks>
    public sealed class PopupSystem : MonoBehaviour
    {
        private static class SfxIds
        {
            public const string Open  = "sfx_ui_popup_open";
            public const string Close = "sfx_ui_popup_close";
        }

        [Tooltip("The popup prefabs this manager can open.")]
        [SerializeField] private PopupRegistry _registry;

        [Header("Backdrop")]
        [Tooltip("The one dimmer, kept directly behind the topmost popup.")]
        [SerializeField] private CanvasGroup _backdrop;

        [Tooltip("Tapping the backdrop closes the topmost popup.")]
        [SerializeField] private Button _backdropButton;

        [Range(0f, 1f)]
        [SerializeField] private float _backdropAlpha = 0.65f;
        [SerializeField] private float _backdropFade  = 0.15f;

        /// <summary>
        /// The open popups, oldest first; the last one is the one the player sees. The only
        /// record of what is open.
        /// </summary>
        private readonly List<Popup> _open = new List<Popup>();

        private IObjectResolver _resolver;
        private IAudioService   _audio;

        [Inject]
        public void Construct(IObjectResolver resolver, IAudioService audio)
        {
            _resolver = resolver;
            _audio    = audio;
        }

        private void Awake()
        {
            _backdropButton.onClick.AddListener(CloseTopPopup);
            _backdrop.alpha = 0f;
            _backdrop.gameObject.SetActive(false);
        }

        /// <summary>
        /// Create a new popup of type <typeparamref name="T"/>, <b>hidden</b>. Nothing appears
        /// yet: the popup has no data. Its <c>Show</c> or <c>Ask</c> fills it and puts it on
        /// screen, on top of any popup already open.
        /// </summary>
        /// <example>
        /// <code>var choice = await _popups.Create&lt;ConfirmPopup&gt;().Ask(new ConfirmPopupData(title, body));</code>
        /// </example>
        /// <exception cref="InvalidOperationException">
        /// The popup is not in the registry. A setup mistake, so it is reported loudly rather
        /// than skipped — and whatever the caller would have done after a "yes" never runs.
        /// </exception>
        public T Create<T>() where T : Popup
        {
            if (_registry == null)
                throw new InvalidOperationException("[PopupSystem] No popup registry is assigned.");

            T popup = _resolver.Instantiate(_registry.Find<T>(), transform);
            popup.gameObject.SetActive(false);   // hidden until its Show / Ask

            popup.Shown  += () => OnPopupShown(popup);
            popup.Closed += () => OnPopupClosed(popup);
            return popup;
        }

        private void CloseTopPopup()
        {
            if (_open.Count > 0) _open[_open.Count - 1].Close();
        }

        private void OnPopupShown(Popup popup)
        {
            _open.Add(popup);
            MoveBackdropBehindTopPopup();
            _audio.PlaySfx(SfxIds.Open);
        }

        private void OnPopupClosed(Popup popup)
        {
            // Quitting: the manager and its popups are being destroyed together.
            if (this == null) return;

            // Closed before it was ever shown: it was never on screen, so nothing to update.
            if (!_open.Remove(popup)) return;

            _audio.PlaySfx(SfxIds.Close);
            MoveBackdropBehindTopPopup();

            // Keep the closing popup in front of the backdrop while it animates out.
            popup.transform.SetAsLastSibling();
        }

        /// <summary>Put the backdrop directly behind the topmost popup, or fade it out if none is open.</summary>
        private void MoveBackdropBehindTopPopup()
        {
            _backdrop.DOKill();

            if (_open.Count == 0)
            {
                _backdrop.DOFade(0f, _backdropFade)
                         .SetUpdate(true)
                         .SetLink(gameObject)
                         .OnComplete(() => _backdrop.gameObject.SetActive(false));
                return;
            }

            _backdrop.gameObject.SetActive(true);
            _backdrop.DOFade(_backdropAlpha, _backdropFade)
                     .SetUpdate(true)
                     .SetLink(gameObject);

            // Backdrop to the front, then the top popup in front of it: the backdrop ends up one
            // place behind the top popup and in front of every older one.
            _backdrop.transform.SetAsLastSibling();
            _open[_open.Count - 1].transform.SetAsLastSibling();
        }
    }
}
