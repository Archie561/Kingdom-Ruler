using System;
using System.Collections.Generic;

namespace KingdomRuler.Systems.Events
{
    /// <summary>
    /// Minimal typed pub/sub for cross-module notifications (ARCHITECTURE.md §4.2).
    ///
    /// This class is the mechanism only — no event message types live beside it.
    /// Cross-module ledger events live in Systems/Ledger/Scripts/Events; module-local
    /// notifications should usually be a plain <c>event Action</c> on the publisher
    /// rather than a message here.
    /// </summary>
    public sealed class EventBus
    {
        private readonly Dictionary<Type, List<Delegate>> _subscribers = new();

        public void Subscribe<T>(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var type = typeof(T);
            if (!_subscribers.TryGetValue(type, out var list))
            {
                list = new List<Delegate>();
                _subscribers[type] = list;
            }
            list.Add(handler);
        }

        public void Unsubscribe<T>(Action<T> handler)
        {
            if (handler == null) return;
            if (_subscribers.TryGetValue(typeof(T), out var list))
                list.Remove(handler);
        }

        /// <summary>
        /// Deliver an event to every current subscriber, synchronously.
        /// </summary>
        /// <remarks>
        /// <para>Delivery iterates a copy, so a handler may subscribe or unsubscribe while
        /// it runs. The copy costs one small array per publish; that is deliberate. These
        /// events fire on player actions and idle accrual — not per frame (ARCHITECTURE.md
        /// §9) — so the allocation is negligible next to getting re-entrancy right.</para>
        ///
        /// <para><b>Exception isolation.</b> One throwing subscriber must not rob the
        /// others of the event, and must not unwind into its publisher. The Ledger
        /// publishes immediately after mutating, so an escaping exception would surface
        /// inside a resource operation the caller believes succeeded. Failures are
        /// collected and rethrown once every subscriber has had its turn.</para>
        /// </remarks>
        public void Publish<T>(T evt)
        {
            if (!_subscribers.TryGetValue(typeof(T), out var list) || list.Count == 0)
                return;

            var snapshot = list.ToArray();
            List<Exception> failures = null;

            foreach (var handler in snapshot)
            {
                // Skip anything unsubscribed by an earlier handler in this same delivery.
                if (!list.Contains(handler)) continue;

                try
                {
                    ((Action<T>)handler)(evt);
                }
                catch (Exception ex)
                {
                    (failures ??= new List<Exception>()).Add(ex);
                }
            }

            if (failures == null) return;
            if (failures.Count == 1) throw failures[0];
            throw new AggregateException(
                $"{failures.Count} subscribers of {typeof(T).Name} threw.", failures);
        }

        /// <summary>Remove every subscriber. For teardown and test isolation.</summary>
        public void Clear() => _subscribers.Clear();
    }
}
