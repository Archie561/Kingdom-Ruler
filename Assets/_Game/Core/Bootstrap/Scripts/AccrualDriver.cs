using VContainer.Unity;
using KingdomRuler.Modules.Laws;
using KingdomRuler.Modules.Trade;

namespace KingdomRuler.Core
{
    /// <summary>
    /// Pumps every time-driven system from the player loop: warehouse regeneration, the law
    /// queue's replenishment timer, and the trade offer refresh.
    /// </summary>
    /// <remarks>
    /// <para><b>One driver, not one per module.</b> This replaced <c>LawsTickDriver</c>, whose own
    /// comment named the arrival of a second module's timer as the moment to consolidate. Trade
    /// was that second module, and it needed two things pumped rather than one.</para>
    ///
    /// <para>It lives beside <c>GameStateCoordinator</c> in <c>Core/Bootstrap</c> for the same
    /// stated reason: it knows every module, so it belongs with the composition root rather than
    /// inside any one of them. The <c>KingdomRuler.Bootstrap</c> assembly already references
    /// them all.</para>
    ///
    /// <para><b>The order in <see cref="Tick"/> is deliberate.</b> Ledger regeneration settles
    /// first so that any module reacting to a matured timer sees current resource amounts rather
    /// than the previous tick's. Keeping that ordering inside one method is the point of merging
    /// the drivers: with an <c>ITickable</c> per system, the relative order would be decided by
    /// VContainer's registration order, and a correctness dependency would live implicitly in the
    /// bootstrapper. Trade in particular settles regen inside its own <c>ProcessTick</c> too, so
    /// accepting an offer never races the driver.</para>
    ///
    /// <para><b>Why a coarse interval.</b> These are minute-scale timers resolved from stored
    /// timestamps, so ticking every frame is wasted battery on a game people leave installed and
    /// check a few times a day (<c>GDD.md</c> §3). Four times a second is far finer than any of
    /// them need and still feels instant.</para>
    ///
    /// <para><b>No resume hook is needed.</b> Every system computes elapsed time from a stored UTC
    /// timestamp rather than accumulating deltas, so the first tick after the app returns to the
    /// foreground settles the whole absence in one step, whether that was ten seconds or ten
    /// hours (<c>ARCHITECTURE.md</c> §4.5).</para>
    ///
    /// <para>Note the cost this accepts: modules can no longer tune their own tick rate. Both
    /// want ~4 Hz today. If one ever genuinely needs a different cadence, that is the trigger to
    /// split it back out — not before.</para>
    /// </remarks>
    public sealed class AccrualDriver : ITickable
    {
        private const float TickIntervalSeconds = 0.25f;

        private readonly LawsManager  _laws;
        private readonly TradeManager _trade;

        private float _secondsSinceLastTick;

        public AccrualDriver(LawsManager laws, TradeManager trade)
        {
            _laws  = laws;
            _trade = trade;
        }

        public void Tick()
        {
            // Unscaled: these are wall-clock mechanics and must not stop if anything ever sets
            // Time.timeScale to zero.
            _secondsSinceLastTick += UnityEngine.Time.unscaledDeltaTime;
            if (_secondsSinceLastTick < TickIntervalSeconds) return;

            _secondsSinceLastTick = 0f;

            // Trade settles warehouse regeneration as well as its own offer timer — see
            // TradeManager.ProcessTick. Regen is Ledger-owned math; Trade owns the schedule.
            _trade.ProcessTick();
            _laws.ProcessReplenishment();
        }
    }
}
