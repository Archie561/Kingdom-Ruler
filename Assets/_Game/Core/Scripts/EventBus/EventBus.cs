using System;
using System.Collections.Generic;

namespace KingdomRuler.Core
{
    public sealed class EventBus
    {
        private readonly Dictionary<Type, List<Delegate>> _subscribers = new();

        public void Subscribe<T>(Action<T> handler)
        {
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
            var type = typeof(T);
            if (_subscribers.TryGetValue(type, out var list))
            {
                list.Remove(handler);
            }
        }

        public void Publish<T>(T evt)
        {
            var type = typeof(T);
            if (_subscribers.TryGetValue(type, out var list))
            {
                // Iterate over a copy to allow subscribe/unsubscribe during publish
                foreach (var handler in list.ToArray())
                {
                    ((Action<T>)handler)(evt);
                }
            }
        }
    }
}
