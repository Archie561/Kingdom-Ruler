using System;
using KingdomRuler.Systems.Audio;
using KingdomRuler.Systems.Haptics;

namespace KingdomRuler.Modules.Navigation
{
    /// <summary>
    /// Which bottom-nav screen is showing, and the feel that goes with changing it. The Model of
    /// the navigation module: plain C#, directly unit-testable.
    /// </summary>
    /// <remarks>
    /// <para><b>Sound and haptics are raised here, not in the Views</b> — the plain-C# layer owns
    /// feel hooks (<c>ARCHITECTURE.md</c> §4.4) — and behind the same early-out as the event, so
    /// "re-tapping the current tab is silent" is covered by an EditMode test.</para>
    ///
    /// <para><b>No Presenter, deliberately</b>: there is nothing to mediate yet. Add one when a tab
    /// needs derived state, such as a card-count badge (<c>ARCHITECTURE.md</c> §4.6).</para>
    /// </remarks>
    public sealed class ScreenNavigator
    {
        // Plain strings until the audio system defines typed sound ids (docs/modules/Laws.md §9).
        private static class SfxIds
        {
            public const string TabSelect = "sfx_nav_tab_select";
        }

        /// <summary>The screen shown on launch.</summary>
        /// <remarks>
        /// Becomes <see cref="ScreenId.Kingdom"/>, the home screen per <c>GDD.md</c> §13, once that
        /// screen exists; until then the app opens on a built one.
        /// </remarks>
        public const ScreenId DefaultScreen = ScreenId.Laws;

        private readonly IAudioService  _audio;
        private readonly IHapticService _haptics;

        /// <summary>The screen currently being shown.</summary>
        /// <remarks>Starts at <see cref="DefaultScreen"/> without an event or a sound: launching is not a player action.</remarks>
        public ScreenId Current { get; private set; } = DefaultScreen;

        /// <summary>
        /// Raised after <see cref="Current"/> changes, carrying the new screen.
        /// </summary>
        /// <remarks>
        /// A plain event, not a bus message: every subscriber already holds the navigator
        /// (<c>ARCHITECTURE.md</c> §4.2).
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
        /// Re-selecting the current screen is a no-op — no event, no sound, no haptic — checked here
        /// once rather than in every View. A tab change is a confirmed action and gets the light
        /// haptic GDD §3 asks for; a rejected re-tap does not.
        /// </remarks>
        public bool Show(ScreenId screen)
        {
            if (Current == screen) return false;

            Current = screen;

            // Notify only after the state has settled: subscribers run inside this call.
            CurrentChanged?.Invoke(screen);

            _audio.PlaySfx(SfxIds.TabSelect);
            _haptics.TriggerLight();
            return true;
        }
    }
}
