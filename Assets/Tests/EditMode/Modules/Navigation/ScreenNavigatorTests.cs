using System;
using System.Collections.Generic;
using NUnit.Framework;
using KingdomRuler.Modules.Navigation;
using KingdomRuler.Tests.EditMode.Systems;   // FakeAudioService, FakeHapticService

namespace KingdomRuler.Tests.EditMode.Modules.Navigation
{
    /// <summary>
    /// Covers the navigation Model. The Views (<c>BottomNavBarView</c>,
    /// <c>ScreenSwitcherView</c>) have no automated coverage — MonoBehaviour plus DOTween is
    /// PlayMode territory, consistent with the Laws Views.
    /// </summary>
    public sealed class ScreenNavigatorTests
    {
        private FakeAudioService  _audio;
        private FakeHapticService _haptics;

        private ScreenNavigator CreateNavigator()
        {
            _audio   = new FakeAudioService();
            _haptics = new FakeHapticService();
            return new ScreenNavigator(_audio, _haptics);
        }

        /// <summary>Any screen that is not the default, so tests never accidentally no-op.</summary>
        private static ScreenId SomeOtherScreen =>
            ScreenNavigator.DefaultScreen == ScreenId.Trade ? ScreenId.Laws : ScreenId.Trade;

        // ── Construction ──────────────────────────────────────────────────────────

        [Test]
        public void NewNavigator_StartsOnTheDefaultScreen()
        {
            var navigator = CreateNavigator();

            Assert.AreEqual(ScreenNavigator.DefaultScreen, navigator.Current);
        }

        [Test]
        public void Constructing_IsSilent()
        {
            // Opening the app is not a player action. Current is assigned directly rather than
            // through Show() precisely so booting raises no event and makes no noise.
            var navigator = CreateNavigator();

            Assert.IsEmpty(_audio.PlayedSfxIds, "Startup should not play a tab-select sound.");
            Assert.AreEqual(0, _haptics.LightTriggerCount,
                "Startup should not fire a haptic.");
            Assert.IsNotNull(navigator);
        }

        [Test]
        public void Constructor_RejectsNullServices()
        {
            Assert.Throws<ArgumentNullException>(
                () => new ScreenNavigator(null, new FakeHapticService()));
            Assert.Throws<ArgumentNullException>(
                () => new ScreenNavigator(new FakeAudioService(), null));
        }

        // ── Switching ─────────────────────────────────────────────────────────────

        [Test]
        public void Show_MovesToTheRequestedScreen()
        {
            var navigator = CreateNavigator();

            bool changed = navigator.Show(SomeOtherScreen);

            Assert.IsTrue(changed);
            Assert.AreEqual(SomeOtherScreen, navigator.Current);
        }

        [Test]
        public void Show_RaisesCurrentChangedOnce_CarryingTheNewScreen()
        {
            var navigator = CreateNavigator();
            var raised = new List<ScreenId>();
            navigator.CurrentChanged += raised.Add;

            navigator.Show(SomeOtherScreen);

            Assert.AreEqual(1, raised.Count);
            Assert.AreEqual(SomeOtherScreen, raised[0]);
        }

        [Test]
        public void Show_UpdatesCurrentBeforeSubscribersRun()
        {
            // The event is synchronous, so a subscriber executes inside Show(). Both Views read
            // state during that call — ScreenSwitcherView decides which roots to activate — so
            // a half-applied Current would render the previous screen and leave the display
            // permanently one tab behind (ARCHITECTURE.md §4.3, same rule as the Ledger).
            var navigator = CreateNavigator();
            ScreenId observed = ScreenNavigator.DefaultScreen;
            navigator.CurrentChanged += _ => observed = navigator.Current;

            navigator.Show(SomeOtherScreen);

            Assert.AreEqual(SomeOtherScreen, observed);
        }

        [Test]
        public void EveryScreenId_IsReachable()
        {
            var navigator = CreateNavigator();

            foreach (ScreenId screen in Enum.GetValues(typeof(ScreenId)))
            {
                navigator.Show(screen);
                Assert.AreEqual(screen, navigator.Current, $"Could not navigate to {screen}.");
            }
        }

        // ── The redundant-tap contract ────────────────────────────────────────────

        [Test]
        public void Show_CurrentScreen_ReportsNoChangeAndRaisesNothing()
        {
            // Both Views hang their whole reaction off CurrentChanged. Without this early-out,
            // tapping the tab you are already on would replay the screen's fade-in.
            var navigator = CreateNavigator();
            int raisedCount = 0;
            navigator.CurrentChanged += _ => raisedCount++;

            bool changed = navigator.Show(navigator.Current);

            Assert.IsFalse(changed);
            Assert.AreEqual(0, raisedCount);
        }

        [Test]
        public void Show_CurrentScreen_IsSilent()
        {
            // GDD §3 warns against overusing haptics. A rejected re-tap is not a confirmed
            // player action, and this is the guard that keeps it quiet — in one place, rather
            // than repeated in every View that could otherwise forget it.
            var navigator = CreateNavigator();

            navigator.Show(navigator.Current);

            Assert.IsEmpty(_audio.PlayedSfxIds);
            Assert.AreEqual(0, _haptics.LightTriggerCount);
        }

        // ── Feel hooks (GDD §3) ───────────────────────────────────────────────────

        [Test]
        public void Show_PlaysExactlyOneSoundAndOneLightHaptic()
        {
            var navigator = CreateNavigator();

            navigator.Show(SomeOtherScreen);

            Assert.AreEqual(1, _audio.PlayedSfxIds.Count);
            Assert.AreEqual(1, _haptics.LightTriggerCount);
            Assert.AreEqual(0, _haptics.MediumTriggerCount,
                "GDD §3 specifies a light impact for confirmed actions.");
        }

        [Test]
        public void RepeatedSwitching_PlaysOneSoundPerActualChange()
        {
            var navigator = CreateNavigator();

            navigator.Show(SomeOtherScreen);                  // change
            navigator.Show(SomeOtherScreen);                  // no-op
            navigator.Show(ScreenNavigator.DefaultScreen);    // change

            Assert.AreEqual(2, _audio.PlayedSfxIds.Count);
            Assert.AreEqual(2, _haptics.LightTriggerCount);
        }

    }
}
