using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer.Unity;

namespace KingdomRuler.Systems.Clock
{
    /// <summary>
    /// The real clock: reads system time, and raises <see cref="Ticked"/> four times a second.
    /// </summary>
    /// <remarks>
    /// <para><b>Systems subscribe to the clock; the clock knows none of them.</b> A manager with a
    /// timer subscribes in its own constructor, so adding a time-driven module never means
    /// editing anything outside it. This replaced a central driver in <c>Core</c> that called
    /// each manager by name — and had silently missed the two modules added after it.</para>
    ///
    /// <para><b>Why a coarse interval.</b> Every timer here is minute-scale and settles from a
    /// stored timestamp, so ticking every frame is wasted battery on a game people leave installed
    /// and check a few times a day (<c>GDD.md</c> §3). Four times a second is far finer than any
    /// of them need. A subscriber that wants less can count ticks; nothing needs that today.</para>
    ///
    /// <para><b>One time per tick.</b> Every subscriber is handed the same <c>now</c>, so the
    /// systems settled in one tick agree on what time it is.</para>
    ///
    /// <para><b>Unscaled</b>: these are wall-clock mechanics and must not stop if anything ever
    /// sets <c>Time.timeScale</c> to zero.</para>
    ///
    /// <para><b>No resume hook.</b> Because everything settles from stored timestamps, the first
    /// tick after the app returns to the foreground settles the whole absence in one step.</para>
    /// </remarks>
    public sealed class GameClock : IClock, ITickable
    {
        private const float TickIntervalSeconds = 0.25f;

        private float _secondsSinceLastTick;

        /// <summary>
        /// The subscribers, copied whenever one subscribes or unsubscribes rather than on every
        /// tick. Subscriptions change a handful of times at startup and ticks happen forever, so
        /// this way a tick allocates nothing (<c>GetInvocationList</c> allocated ~70 bytes each).
        /// </summary>
        private Action<DateTime>[] _subscribers = Array.Empty<Action<DateTime>>();

        public DateTime UtcNow => DateTime.UtcNow;

        public event Action<DateTime> Ticked
        {
            add
            {
                var subscribers = new List<Action<DateTime>>(_subscribers) { value };
                _subscribers = subscribers.ToArray();
            }
            remove
            {
                var subscribers = new List<Action<DateTime>>(_subscribers);
                subscribers.Remove(value);
                _subscribers = subscribers.ToArray();
            }
        }

        void ITickable.Tick()
        {
            _secondsSinceLastTick += Time.unscaledDeltaTime;
            if (_secondsSinceLastTick < TickIntervalSeconds) return;
            _secondsSinceLastTick = 0f;

            NotifySubscribers(UtcNow);
        }

        private void NotifySubscribers(DateTime now)
        {
            // Each subscriber on its own: with a plain multicast call, one that throws would
            // skip every subscriber after it, and a bug in one module would freeze every other
            // module's timers.
            foreach (var subscriber in _subscribers)
            {
                try { subscriber(now); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }
    }
}
