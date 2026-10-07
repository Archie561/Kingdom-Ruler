using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KingdomRuler.Modules.Navigation
{
    /// <summary>
    /// One tab in the bottom nav bar: an icon, a label, and its selected/unselected look.
    /// Reports taps to <see cref="BottomNavBarView"/>; owns its own DOTween animations and no
    /// state beyond what it draws.
    /// </summary>
    /// <remarks>
    /// <para>The label's text is <b>not</b> set from here. It is fixed chrome, so a
    /// <c>LocalizeStringEvent</c> component on the label renders it and keeps it correct across
    /// a locale change with no code (<c>ARCHITECTURE.md</c> §2). Point that component at the
    /// <c>NavigationUITable</c> entry <c>nav.&lt;screenid&gt;</c>. Nothing checks these
    /// automatically: a missing label is on screen the moment the bar appears.</para>
    ///
    /// <para><b>The tab owns the screen it opens.</b> <see cref="ScreenId"/> carries all five
    /// screens from <c>GDD.md</c> §13 while only Laws and Trade are built, so the unbuilt three
    /// simply have no <see cref="_screen"/> assigned — which is what makes them unavailable.
    /// Availability is <em>derived</em>, never authored: a separate "is available" flag would be
    /// a second fact that has to agree with the reference, and the two would eventually
    /// disagree (the reasoning in <c>docs/modules/Laws.md</c> §4 about deriving one count from
    /// the other). Making a tab live is therefore one action — drag its screen in.</para>
    ///
    /// <para>If a tab ever needs to be disabled while its screen <em>does</em> exist — a
    /// progression lock, say — that is a genuinely different concept and gets its own
    /// <c>IsUnlocked</c>, rather than being folded back into this one.</para>
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

        /// <summary>
        /// Whether this tab's screen exists and can be navigated to. Derived, never stored —
        /// see the class remarks.
        /// </summary>
        /// <remarks>
        /// Explicit <c>!= null</c> rather than a null-coalescing form, because
        /// <see cref="_screen"/> is a <c>UnityEngine.Object</c>: <c>??</c> bypasses Unity's
        /// overloaded equality and would report a <em>destroyed</em> screen as available.
        /// </remarks>
        public bool IsAvailable => _screen != null;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            if (_button == null) _button = GetComponent<Button>();

            _button.interactable = IsAvailable;
            _button.onClick.AddListener(HandleClick);

            // Paint the resting state before the first frame, so an unavailable tab is never
            // briefly drawn as a normal one.
            ApplyVisualState(animate: false);
        }

        private void OnDestroy()
        {
            if (_button != null) _button.onClick.RemoveListener(HandleClick);

            // A tween outliving the object it animates throws on its next step. The nav bar
            // survives every scene the player sees, so this only matters on teardown — which
            // is exactly when it would be missed.
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

            // Unscaled time, same reason as the screen fade: tab feedback must not stall
            // half-way if Time.timeScale is ever zeroed.
            _rect.DOScale(scale, _tweenDuration).SetEase(Ease.OutBack).SetUpdate(true);
            if (_icon != null) _icon.DOColor(target, _tweenDuration).SetUpdate(true);
            if (_label != null) _label.DOColor(target, _tweenDuration).SetUpdate(true);
        }

        private void HandleClick()
        {
            // Belt and braces: Button.interactable already blocks this, but a tab that fired
            // for an unbound screen would fail somewhere far less obvious.
            if (!IsAvailable) return;
            OnPressed?.Invoke(_screenId);
        }
    }
}
