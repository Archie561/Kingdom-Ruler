using System.Collections.Generic;
using UnityEngine;
using VContainer;

namespace KingdomRuler.Modules.Navigation
{
    /// <summary>
    /// The bottom tab bar (<c>GDD.md</c> §13). Forwards taps to <see cref="ScreenNavigator"/>,
    /// then reacts to it: lights the selected tab and shows that screen while hiding the rest.
    /// </summary>
    /// <remarks>
    /// <para><b>Each tab carries the screen it opens</b> (<see cref="NavTabButton.Screen"/>), so
    /// this holds one array and one lookup, and a tab is available exactly when its screen is set.
    /// Tabs are keyed by <see cref="ScreenId"/>, never by array index.</para>
    ///
    /// <para>Display and input only: sound and haptics belong to the navigator, so a tap on the
    /// selected tab is silent by construction (<see cref="ScreenNavigator.Show"/>).</para>
    ///
    /// <para><b>Switching is <c>SetActive</c>, not a Canvas toggle</b>, so each screen's own
    /// <c>OnEnable</c>/<c>OnDisable</c> fire — that is how a screen gates its audio. The switch is a
    /// hard cut, a known deviation from <c>GDD.md</c> §3 (<c>ARCHITECTURE.md</c> §4.6).</para>
    ///
    /// <para>The prefab's Canvas sorts at 100 so the bar draws above every screen, including
    /// Laws' <c>CardCanvas</c> at 10.</para>
    /// </remarks>
    public sealed class BottomNavBarView : MonoBehaviour
    {
        [Header("Tabs")]
        [Tooltip("Every tab in the bar. Each one carries the screen it opens, so this is the " +
                 "only list to maintain. Display order comes from the layout, not this array.")]
        [SerializeField] private NavTabButton[] _tabs;

        private ScreenNavigator _navigator;

        private readonly Dictionary<ScreenId, NavTabButton> _tabsById = new();

        [Inject]
        public void Construct(ScreenNavigator navigator)
        {
            _navigator = navigator;
        }

        private void Start()
        {
            // Almost always Play mode entered from Main instead of Bootstrap.
            if (_navigator == null)
            {
                Debug.LogError(
                    "[BottomNavBarView] No ScreenNavigator was injected — navigation is " +
                    "disabled and every screen will stay visible at once. Enter Play mode from " +
                    "the Bootstrap scene; Main is loaded additively from there and only then " +
                    "do its Views get injected.", this);
                enabled = false;
                return;
            }

            BuildTabLookup();
            Validate();
            WireTabEvents();

            _navigator.CurrentChanged += Apply;

            // Screens are authored active and switched off here, so injection never depends on
            // whether InjectGameObject walks inactive children. No animation: the opening screen
            // is not a player action.
            ApplyScreens(_navigator.Current);
            ApplySelection(_navigator.Current, animate: false);
        }

        private void OnDestroy()
        {
            UnwireTabEvents();

            // The navigator outlives this View.
            if (_navigator != null) _navigator.CurrentChanged -= Apply;
        }

        private void Apply(ScreenId target)
        {
            ApplyScreens(target);
            ApplySelection(target, animate: true);
        }

        /// <summary>Show exactly <paramref name="target"/>'s screen, hide every other.</summary>
        private void ApplyScreens(ScreenId target)
        {
            if (!_tabsById.TryGetValue(target, out var targetTab) || targetTab.Screen == null)
            {
                // Leave the current screen up: a blank app is worse than a stuck tab.
                Debug.LogError(
                    $"[BottomNavBarView] No screen is bound to '{target}', so it cannot be " +
                    "shown. Assign that screen's root on the matching tab.", this);
                return;
            }

            foreach (var entry in _tabsById)
            {
                var screen = entry.Value.Screen;
                if (screen != null) screen.SetActive(entry.Key == target);
            }
        }

        private void ApplySelection(ScreenId selected, bool animate)
        {
            foreach (var entry in _tabsById)
                entry.Value.SetSelected(entry.Key == selected, animate);
        }

        /// <summary>A tab was tapped. The navigator decides whether that is a change.</summary>
        private void HandleTabPressed(ScreenId screen) => _navigator.Show(screen);

        private void WireTabEvents()
        {
            // Method group, so OnDestroy can unsubscribe the same delegate.
            foreach (var tab in _tabsById.Values)
                tab.OnPressed += HandleTabPressed;
        }

        private void UnwireTabEvents()
        {
            foreach (var tab in _tabsById.Values)
                if (tab != null) tab.OnPressed -= HandleTabPressed;
        }

        private void BuildTabLookup()
        {
            _tabsById.Clear();
            if (_tabs == null) return;

            foreach (var tab in _tabs)
            {
                if (tab == null) continue;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (_tabsById.ContainsKey(tab.ScreenId))
                    Debug.LogError(
                        $"[BottomNavBarView] Two tabs both declare '{tab.ScreenId}'. Each " +
                        "ScreenId must be unique — one of them will never light up.", this);
#endif
                _tabsById[tab.ScreenId] = tab;
            }
        }

        private void Validate()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_tabsById.Count == 0)
                Debug.LogError("[BottomNavBarView] No tabs are assigned.", this);

            // Checked at boot: the app would otherwise open on nothing.
            if (!_tabsById.TryGetValue(ScreenNavigator.DefaultScreen, out var defaultTab)
                || defaultTab.Screen == null)
            {
                Debug.LogError(
                    $"[BottomNavBarView] The default screen " +
                    $"('{ScreenNavigator.DefaultScreen}') has no screen assigned on its tab, " +
                    "so the app will launch with nothing visible.", this);
            }

#endif
        }
    }
}
