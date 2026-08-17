using System.Collections.Generic;
using UnityEngine;
using VContainer;

namespace KingdomRuler.Shared.Navigation
{
    /// <summary>
    /// The bottom tab bar (<c>GDD.md</c> §13). Forwards taps to <see cref="ScreenNavigator"/>,
    /// then reacts to it: lights the selected tab and shows that screen while hiding the rest.
    /// </summary>
    /// <remarks>
    /// <para><b>Each tab carries the screen it opens</b> (<see cref="NavTabButton.Screen"/>), so
    /// this component holds one array and one lookup. A tab and its screen cannot disagree about
    /// which <see cref="ScreenId"/> they serve, and "this tab is available" is not a separate
    /// flag that could contradict the reference — it simply means the reference is set.</para>
    ///
    /// <para>Display and input only. It plays no sound and fires no haptic — those belong to
    /// the navigator, so a tap on the already-selected tab is silent by construction
    /// (<see cref="ScreenNavigator.Show"/>).</para>
    ///
    /// <para><b>Switching is <c>SetActive</c>, not a Canvas toggle.</b> That is what makes a
    /// screen's own <c>OnEnable</c>/<c>OnDisable</c> fire, which is how <c>LawsView</c> already
    /// tells its Presenter to gate audio — a card arriving on another tab updates the queue
    /// silently. Disabling only the Canvas would skip those callbacks and leave each View's
    /// <c>Update</c> ticking unseen. The mechanics are unaffected either way: timers belong to
    /// tick drivers, not Views (<c>ARCHITECTURE.md</c> §4.5).</para>
    ///
    /// <para><b>The switch is a hard cut.</b> There is no screen transition, which is a known
    /// deviation from <c>GDD.md</c> §3's "animation on every state change" — recorded in
    /// <c>ARCHITECTURE.md</c> §4.6 rather than left as a silent gap. The tab itself still
    /// animates (<see cref="NavTabButton"/>), so the tap is not without feedback.</para>
    ///
    /// <para>Tabs are keyed by <see cref="ScreenId"/>, never by array index — the lesson from
    /// the Laws characteristic bars (<c>docs/modules/Laws.md</c> §6.6). Index binding survives a
    /// reorder and then quietly shows the wrong thing.</para>
    ///
    /// <para>Canvas note: this prefab's Canvas must sort <b>above</b> every screen. Laws nests a
    /// <c>CardCanvas</c> at sorting order 10 with <c>Override Sorting</c>, so a bar left at the
    /// default 0 is drawn underneath the law card. The prefab ships at 100.</para>
    /// </remarks>
    public sealed class BottomNavBarView : MonoBehaviour
    {
        [Header("Tabs")]
        [Tooltip("Every tab in the bar. Each one carries the screen it opens, so this is the " +
                 "only list to maintain. Display order comes from the layout, not this array.")]
        [SerializeField] private NavTabButton[] _tabs;

        private ScreenNavigator _navigator;

        /// <summary>
        /// Tabs by the screen they open. Each tab also holds that screen, so this one lookup
        /// serves both jobs — there is no second map of ids to GameObjects to fall out of step.
        /// </summary>
        private readonly Dictionary<ScreenId, NavTabButton> _tabsById = new();

        [Inject]
        public void Construct(ScreenNavigator navigator)
        {
            _navigator = navigator;
        }

        private void Start()
        {
            // Nothing injected us — almost always Play mode entered from Main instead of
            // Bootstrap, so the root LifetimeScope never existed. Say so once and switch off,
            // rather than null-reffing on the first tap.
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

            // Screens are authored ACTIVE in the scene and switched off here. Authoring them
            // inactive instead would depend on whether VContainer's InjectGameObject walks
            // inactive children, and a boot sequence that silently leaves a View uninjected is
            // a bad thing to build on a maybe.
            //
            // The tabs are painted without animating: the opening screen is not a player action.
            ApplyScreens(_navigator.Current);
            ApplySelection(_navigator.Current, animate: false);
        }

        private void OnDestroy()
        {
            UnwireTabEvents();

            // The navigator is a root-scope singleton that outlives this View, so a missed
            // unsubscribe leaks into the next scene load.
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
                // Leave the current screen up rather than hiding everything: a blank app is a
                // worse failure than a stuck tab, and the message says exactly what to fix.
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
            // Method group, not a lambda: a lambda creates a fresh delegate each time and
            // could never be unsubscribed in OnDestroy.
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

            // Reachable at boot, unlike the tap-time error above: the app would open on
            // nothing at all, with no input to blame.
            if (!_tabsById.TryGetValue(ScreenNavigator.DefaultScreen, out var defaultTab)
                || defaultTab.Screen == null)
            {
                Debug.LogError(
                    $"[BottomNavBarView] The default screen " +
                    $"('{ScreenNavigator.DefaultScreen}') has no screen assigned on its tab, " +
                    "so the app will launch with nothing visible.", this);
            }

            // Note there is deliberately no "tab says available but has no screen" check:
            // availability *is* having a screen, so that state cannot be authored.
#endif
        }
    }
}
