using VContainer.Unity;

namespace KingdomRuler.Modules.Laws
{
    /// <summary>
    /// Drives the law queue's replenishment timer from the player loop.
    /// </summary>
    /// <remarks>
    /// <para>This exists so the timer does not depend on any View being alive
    /// (ARCHITECTURE.md §4.5). It used to be pumped from <c>LawsView.Update()</c>, which
    /// tied the mechanic's progress to whether the Laws screen happened to be loaded.</para>
    ///
    /// <para><b>Why a coarse interval.</b> Replenishment is a two-minute timer resolved from
    /// timestamps, so ticking it every frame is pure wasted battery on a game people leave
    /// installed and check a few times a day (GDD §3). Four times a second is far finer than
    /// the mechanic needs and still feels instant.</para>
    ///
    /// <para><b>No resume hook is needed.</b> <see cref="LawsManager.ProcessReplenishment"/>
    /// computes elapsed time from a stored UTC timestamp rather than accumulating deltas, so
    /// the first tick after the app returns to the foreground catches up on the entire
    /// absence in one step, whether that was ten seconds or ten hours.</para>
    ///
    /// <para>When a second module needs its own timer pumped, that is the moment to consider
    /// a single shared accrual driver rather than one per module — not before.</para>
    /// </remarks>
    public sealed class LawsTickDriver : ITickable
    {
        private const float TickIntervalSeconds = 0.25f;

        private readonly LawsManager _manager;
        private float _secondsSinceLastTick;

        public LawsTickDriver(LawsManager manager)
        {
            _manager = manager;
        }

        public void Tick()
        {
            _secondsSinceLastTick += UnityEngine.Time.unscaledDeltaTime;
            if (_secondsSinceLastTick < TickIntervalSeconds) return;

            _secondsSinceLastTick = 0f;
            _manager.ProcessReplenishment();
        }
    }
}
