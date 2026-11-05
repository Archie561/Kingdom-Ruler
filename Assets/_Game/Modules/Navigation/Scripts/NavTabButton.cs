using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KingdomRuler.Modules.Navigation
{
    /// <summary>
    /// One tab in the bottom nav bar: icon, label, selected/unselected look and its own DOTween
    /// animations. Reports taps to <see cref="BottomNavBarView"/>.
    /// </summary>
    /// <remarks>
    /// <para>The label is fixed chrome: a <c>LocalizeStringEvent</c> on it renders the
    /// <c>NavigationUITable</c> entry <c>nav.&lt;screenid&gt;</c>, with no code
    /// (<c>ARCHITECTURE.md</c> §2).</para>
    ///
    /// <para><b>Availability is derived, never authored:</b> a tab is available exactly when its
    /// screen is assigned, so making it live is one action — drag the screen in. A tab that must be
    /// disabled while its screen exists (a progression lock) is a different concept and gets its
    /// own <c>IsUnlocked</c>.</para>
    /// </remarks>
    [RequireComponent(typeof(Button))]
    public sealed class NavTabButton : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Which screen this tab selects. Must be unique across the nav bar's tabs — " +
                 "the bar binds by this value, not by position in its array.")]
        [SerializeField] private ScreenId _screenId;

        [Tooltip("The screen root this tab opens. Leave empty while that screen has no " +
                 "prefab yet — the tab then renders dimmed and cannot be tapped. Assigning it " +
                 "is the only step needed to make the tab live.")]
        [SerializeField] private GameObject _screen;

        [Header("Sub-views")]
        [SerializeField] private Button _button;
        [SerializeField] private Image _icon;
        [SerializeField] private TextMeshProUGUI _label;

        [Header("Look")]
        [SerializeField] private Color _selectedColor   = Color.white;
        [SerializeField] private Color _unselectedColor = new Color(0.65f, 0.65f, 0.65f, 1f);
        [SerializeField] private Color _unavailableColor = new Color(0.35f, 0.35f, 0.35f, 1f);

        [Tooltip("Scale the tab pops to when selected, relative to its resting size.")]
        [SerializeField] private float _selectedScale = 1.12f;
        [SerializeField] private float _tweenDuration = 0.18f;

        private RectTransform _rect;
        private bool _isSelected;

        /// <summary>Raised when the player taps this tab. Never raised by an unavailable tab.</summary>
        public event Action<ScreenId> OnPressed;

        /// <summary>Which screen this tab selects.</summary>
        public ScreenId ScreenId => _screenId;

        /// <summary>The screen root this tab opens, or null while that screen is unbuilt.</summary>
        public GameObject Screen => _screen;

        /// <summary>Whether this tab's screen exists and can be navigated to.</summary>
        /// <remarks>
        /// Explicit <c>!= null</c>: <see cref="_screen"/> is a <c>UnityEngine.Object</c>, and a
        /// null-coalescing form would bypass Unity's equality and report a destroyed screen as available.
        /// </remarks>
        public bool IsAvailable => _screen != null;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            if (_button == null) _button = GetComponent<Button>();

            _button.interactable = IsAvailable;
            _button.onClick.AddListener(HandleClick);

            // Before the first frame, so an unavailable tab is never briefly drawn as a normal one.
            ApplyVisualState(animate: false);
        }

        private void OnDestroy()
        {
            if (_button != null) _button.onClick.RemoveListener(HandleClick);

            // A tween outliving the object it animates throws on its next step.
            _rect.DOKill();
            if (_icon != null) _icon.DOKill();
            if (_label != null) _label.DOKill();
        }

        /// <summary>
        /// Show this tab as selected or not.
        /// </summary>
        /// <param name="selected">Whether this is the active tab.</param>
        /// <param name="animate">
        /// False when painting the initial state at startup — that is not a player action and
        /// should not tween.
        /// </param>
        public void SetSelected(bool selected, bool animate)
        {
            if (_isSelected == selected && animate) return;
            _isSelected = selected;
            ApplyVisualState(animate);
        }

        private void ApplyVisualState(bool animate)
        {
            bool available = IsAvailable;

            Color target = !available  ? _unavailableColor
                         : _isSelected ? _selectedColor
                         : _unselectedColor;

            // An unavailable tab never grows, even if something tried to select it.
            float scale = _isSelected && available ? _selectedScale : 1f;

            _rect.DOKill();
            if (_icon != null) _icon.DOKill();
            if (_label != null) _label.DOKill();

            if (!animate)
            {
                _rect.localScale = Vector3.one * scale;
                if (_icon != null) _icon.color = target;
                if (_label != null) _label.color = target;
                return;
            }

            // Unscaled, so tab feedback never stalls if Time.timeScale is zeroed.
            _rect.DOScale(scale, _tweenDuration).SetEase(Ease.OutBack).SetUpdate(true);
            if (_icon != null) _icon.DOColor(target, _tweenDuration).SetUpdate(true);
            if (_label != null) _label.DOColor(target, _tweenDuration).SetUpdate(true);
        }

        private void HandleClick()
        {
            // Button.interactable already blocks this; a tab firing for an unbound screen would
            // fail somewhere far less obvious.
            if (!IsAvailable) return;
            OnPressed?.Invoke(_screenId);
        }
    }
}
