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
    /// <para>Systems subscribe to the clock in their own constructors; the clock knows none of
    /// them, so adding a time-driven module never means editing anything outside it.</para>
    ///
    /// <para>Every timer is minute-scale and settles from a stored timestamp, so ticking every
    /// frame would only waste battery (<c>GDD.md</c> §3). Every subscriber gets the same
    /// <c>now</c> in a tick, so systems settled together agree on the time. Ticks run on unscaled
    /// time, so they never stop if <c>Time.timeScale</c> is zeroed.</para>
    /// </remarks>
    public sealed class GameClock : IClock, ITickable
    {
        private const float TickIntervalSeconds = 0.25f;

        private float _secondsSinceLastTick;

        // Copied on subscribe/unsubscribe rather than per tick: subscriptions change a few times at
        // startup and ticks run forever, so a tick allocates nothing.
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
            // One at a time: with a plain multicast call, one subscriber that throws would skip
            // every subscriber after it and freeze the other modules' timers.
            foreach (var subscriber in _subscribers)
            {
                try { subscriber(now); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }
    }
}
