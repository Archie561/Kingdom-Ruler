using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomRuler.Systems.Events
{
    /// <summary>
    /// Minimal typed pub/sub for cross-module notifications (<c>ARCHITECTURE.md</c> §4.2). The
    /// mechanism only: message types live beside the resource they describe.
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
        /// <para>Delivery iterates a copy, so a handler may subscribe or unsubscribe while it runs.
        /// The copy is one small array per publish; events fire on player actions, not per frame.</para>
        ///
        /// <para>A throwing subscriber is logged and skipped — never rethrown. The others still get
        /// the event, and the publisher is never interrupted: the Ledger publishes mid-transaction
        /// (a level-up inside a card's effects), and an exception there would abandon the rest of
        /// the transaction. Same policy as <c>GameClock</c>.</para>
        /// </remarks>
        public void Publish<T>(T evt)
        {
            if (!_subscribers.TryGetValue(typeof(T), out var list) || list.Count == 0)
                return;

            var snapshot = list.ToArray();

            foreach (var handler in snapshot)
            {
                // Skip anything unsubscribed by an earlier handler in this same delivery.
                if (!list.Contains(handler)) continue;

                try
                {
                    ((Action<T>)handler)(evt);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        /// <summary>Remove every subscriber. For teardown and test isolation.</summary>
        public void Clear() => _subscribers.Clear();
    }
}
