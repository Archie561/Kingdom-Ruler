using System;
using System.Collections.Generic;
using KingdomRuler.Core;
using KingdomRuler.Shared.Ledger;
using KingdomRuler.Shared.Services;
using KingdomRuler.Modules.Events.Domain;

namespace KingdomRuler.Modules.Events
{
    public sealed class EventsManager
    {
        private readonly KingdomLedger _ledger;
        private readonly EventBus _eventBus;
        private readonly IClock _clock;
        private readonly EventsConfig _config;
        private readonly Random _rng;

        private readonly List<EventDefinition> _eventPool = new();
        private readonly List<EventDefinition> _pendingEvents = new();
        private DateTime _lastSpawnCheckUtc;

        public IReadOnlyList<EventDefinition> PendingEvents => _pendingEvents;
        public DateTime LastSpawnCheckUtc => _lastSpawnCheckUtc;

        public EventsManager(KingdomLedger ledger, EventBus eventBus, IClock clock, EventsConfig config)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _rng = new Random();
            _lastSpawnCheckUtc = _clock.UtcNow;
        }

        public void InitializeEventPool(IEnumerable<EventDefinition> allEvents)
        {
            _eventPool.Clear();
            foreach (var e in allEvents)
            {
                if (e != null && !string.IsNullOrEmpty(e.EventId))
                    _eventPool.Add(e);
            }
        }

        /// <summary>
        /// Process offline accrual for event spawning.
        /// Spawns events for each interval that has passed.
        /// </summary>
        public void ProcessSpawning()
        {
            if (_config.SpawnIntervalSeconds <= 0) return;

            var now = _clock.UtcNow;
            var elapsed = (float)(now - _lastSpawnCheckUtc).TotalSeconds;

            while (elapsed >= _config.SpawnIntervalSeconds
                   && _pendingEvents.Count < _config.MaxPendingEvents
                   && _eventPool.Count > 0)
            {
                elapsed -= _config.SpawnIntervalSeconds;
                var selected = WeightedEventSelector.Select(_eventPool, _rng);
                if (selected != null)
                    _pendingEvents.Add(selected);
            }

            _lastSpawnCheckUtc = now - TimeSpan.FromSeconds(Math.Max(0, elapsed));
        }

        /// <summary>
        /// Resolve a pending event by choosing A (choiceIndex=0) or B (choiceIndex=1).
        /// Applies outcome effects to the ledger.
        /// </summary>
        public bool ResolveEvent(int eventIndex, int choiceIndex)
        {
            if (eventIndex < 0 || eventIndex >= _pendingEvents.Count) return false;
            if (choiceIndex < 0 || choiceIndex > 1) return false;

            var evt = _pendingEvents[eventIndex];
            var choice = choiceIndex == 0 ? evt.ChoiceA : evt.ChoiceB;

            // Use a simple points-required function for characteristic effects.
            // In production, this would come from LawsConfig leveling curve.
            Func<int, float> pointsRequired = level => 100f;

            EventOutcomeApplier.Apply(_ledger, choice.Effects, pointsRequired);
            _pendingEvents.RemoveAt(eventIndex);
            return true;
        }

        public int PendingCount => _pendingEvents.Count;

        // --- Save/Load ---
        public EventsStateDto ToDto()
        {
            var dto = new EventsStateDto
            {
                LastSpawnCheckUtc = _lastSpawnCheckUtc.ToString("O"),
                PendingEventIds = new List<string>()
            };
            foreach (var e in _pendingEvents)
                dto.PendingEventIds.Add(e.EventId);
            return dto;
        }

        public void LoadFromDto(EventsStateDto dto)
        {
            if (dto == null) return;

            _lastSpawnCheckUtc = DateTime.TryParse(dto.LastSpawnCheckUtc, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed : _clock.UtcNow;

            _pendingEvents.Clear();
            if (dto.PendingEventIds != null)
            {
                var lookup = new Dictionary<string, EventDefinition>();
                foreach (var e in _eventPool)
                    lookup[e.EventId] = e;

                foreach (var id in dto.PendingEventIds)
                {
                    if (lookup.TryGetValue(id, out var evt))
                        _pendingEvents.Add(evt);
                }
            }

            ProcessSpawning();
        }
    }
}
