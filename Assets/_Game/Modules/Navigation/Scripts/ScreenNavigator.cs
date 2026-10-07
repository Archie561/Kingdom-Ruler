using System;
using KingdomRuler.Systems.Audio;
using KingdomRuler.Systems.Haptics;

namespace KingdomRuler.Modules.Navigation
{
    /// <summary>
    /// Which bottom-nav screen is currently showing, and the feel that goes with changing it.
    /// The Model of the navigation mini-module: plain C#, no <c>UnityEngine</c> types, directly
    /// unit-testable.
    /// </summary>
    /// <remarks>
    /// <para>Switching GameObjects on and off is presentation and lives in
    /// <see cref="ScreenSwitcherView"/>; drawing the tab strip lives in
    /// <see cref="BottomNavBarView"/>. What stays here is the state and the one rule worth
    /// protecting — that a redundant switch is not a switch (see <see cref="Show"/>).</para>
    ///
    /// <para><b>Sound and haptics are raised here, not in the Views</b>, matching
    /// <c>LawsPresenter</c>: in this codebase the plain-C# layer owns feel hooks and the View
    /// owns only DOTween (<c>ARCHITECTURE.md</c> §2, §4.4). It also puts them behind the same
    /// early-out that gates the event, so "tapping the tab you are already on is silent" is
    /// covered by an EditMode test instead of by remembering to guard it in the nav bar.</para>
    ///
    /// <para><b>There is no Presenter between this and the Views</b>, and that is a decision
    /// rather than an omission. A Presenter would mediate nothing: tab labels resolve through
    /// <c>LocalizeStringEvent</c> components with no code (<c>ARCHITECTURE.md</c> §2), a tab's
    /// availability is authored on its button, and selection state is exactly
    /// <see cref="Current"/>. Adding a pass-through layer now is the speculative abstraction
    /// <c>CLAUDE.md</c> §1.4 warns against. <b>Add one</b> as soon as a tab needs derived state
    /// — a card-count badge, or availability driven by progression — because that is the point
    /// where a View would otherwise start computing.</para>
    /// </remarks>
    public sealed class ScreenNavigator
    {
        /// <summary>
        /// SFX ids this module raises.
        /// </summary>
        /// <remarks>
        /// Magic strings, matching <c>LawsPresenter.SfxIds</c>. Both are waiting on the audio
        /// system — <c>StubAudioService</c> no-ops today — and both should become whatever
        /// typed identifier that lands with (see <c>docs/modules/Laws.md</c> §9).
        /// </remarks>
        private static class SfxIds
        {
            public const string TabSelect = "sfx_nav_tab_select";
        }

        /// <summary>
        /// The screen shown on launch.
        /// </summary>
        /// <remarks>
        /// <c>GDD.md</c> §13 makes Kingdom the home screen, and this should become
        /// <see cref="ScreenId.Kingdom"/> the moment that screen exists. It is Laws today
        /// because Laws is the only fully built screen, and booting onto an empty placeholder
        /// would make every Play-mode run start on nothing.
        ///
        /// A const rather than a config asset: a whole ScriptableObject to carry one enum value
        /// is the kind of machinery <c>CLAUDE.md</c> §1.4 exists to prevent, and this is a
        /// one-line change when the time comes.
        /// </remarks>
        public const ScreenId DefaultScreen = ScreenId.Laws;

        private readonly IAudioService  _audio;
        private readonly IHapticService _haptics;

        /// <summary>The screen currently being shown.</summary>
        /// <remarks>
        /// Set directly rather than through <see cref="Show"/>, so launching the app raises no
        /// event and makes no noise — the opening screen is not a player action.
        /// </remarks>
        public ScreenId Current { get; private set; } = DefaultScreen;

        /// <summary>
        /// Raised after <see cref="Current"/> changes, carrying the new screen.
        /// </summary>
        /// <remarks>
        /// A plain <c>event Action</c> rather than an <c>EventBus</c> message, per
        /// <c>ARCHITECTURE.md</c> §4.2: every subscriber already holds this object by
        /// injection, so a bus round-trip would buy nothing and cost an unsubscribe. Promote it
        /// to a Ledger-tier event only if something outside the navigation module ever needs to
        /// react to a tab change without being handed the navigator.
        /// </remarks>
        public event Action<ScreenId> CurrentChanged;

        public ScreenNavigator(IAudioService audio, IHapticService haptics)
        {
            _audio   = audio   ?? throw new ArgumentNullException(nameof(audio));
            _haptics = haptics ?? throw new ArgumentNullException(nameof(haptics));
        }

        /// <summary>
        /// Switch to <paramref name="screen"/>. Returns whether anything actually changed.
        /// </summary>
        /// <remarks>
        /// Re-selecting the current screen is a no-op: it raises nothing, plays nothing and
        /// buzzes nothing. This is the whole reason the check lives here rather than in a View.
        /// Both Views react to <see cref="CurrentChanged"/>, so without it, tapping the tab you
        /// are already on would re-trigger the screen's fade-in and click at the player — and
        /// each View would need its own guard, which is two chances to forget.
        ///
        /// GDD §3 asks for a light haptic on confirmed player actions and warns against
        /// overusing them; a tab change is a confirmed action, a rejected re-tap is not.
        /// </remarks>
        public bool Show(ScreenId screen)
        {
            if (Current == screen) return false;

            Current = screen;

            // Notify only once the state has settled: subscribers run inside this call, and a
            // View that re-read Current mid-update would see the old value
            // (ARCHITECTURE.md §4.3, same reasoning as the Ledger).
            CurrentChanged?.Invoke(screen);

            _audio.PlaySfx(SfxIds.TabSelect);
            _haptics.TriggerLight();
            return true;
        }
    }
}
