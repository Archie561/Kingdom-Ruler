using System;
using System.Collections.Generic;
using KingdomRuler.Systems.Events;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Systems.Clock;
using KingdomRuler.Systems.Save;
using KingdomRuler.Modules.RandomOccurrences.Domain;

namespace KingdomRuler.Modules.RandomOccurrences
{
    public sealed class RandomOccurrenceManager
    {
        private readonly KingdomLedger _ledger;
        private readonly EventBus _eventBus;
        private readonly IClock _clock;
        private readonly RandomOccurrenceConfig _config;
        private readonly Random _rng;

        private readonly List<RandomOccurrenceDefinition> _occurrencePool = new();
        private readonly List<RandomOccurrenceDefinition> _pendingOccurrences = new();
        private DateTime _lastSpawnCheckUtc;

        public IReadOnlyList<RandomOccurrenceDefinition> PendingOccurrences => _pendingOccurrences;
        public DateTime LastSpawnCheckUtc => _lastSpawnCheckUtc;

        public RandomOccurrenceManager(KingdomLedger ledger, EventBus eventBus, IClock clock, RandomOccurrenceConfig config)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _rng = new Random();
            _lastSpawnCheckUtc = _clock.UtcNow;
        }

        public void InitializeOccurrencePool(IEnumerable<RandomOccurrenceDefinition> allOccurrences)
        {
            _occurrencePool.Clear();
            foreach (var e in allOccurrences)
            {
                if (e != null && !string.IsNullOrEmpty(e.OccurrenceId))
                    _occurrencePool.Add(e);
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
                   && _pendingOccurrences.Count < _config.MaxPendingOccurrences
                   && _occurrencePool.Count > 0)
            {
                elapsed -= _config.SpawnIntervalSeconds;
                var selected = WeightedOccurrenceSelector.Select(_occurrencePool, _rng);
                if (selected != null)
                    _pendingOccurrences.Add(selected);
            }

            _lastSpawnCheckUtc = now - TimeSpan.FromSeconds(Math.Max(0, elapsed));
        }

        /// <summary>
        /// Resolve a pending event by choosing A (choiceIndex=0) or B (choiceIndex=1).
        /// Applies outcome effects to the ledger.
        /// </summary>
        public bool ResolveOccurrence(int eventIndex, int choiceIndex)
        {
            if (eventIndex < 0 || eventIndex >= _pendingOccurrences.Count) return false;
            if (choiceIndex < 0 || choiceIndex > 1) return false;

            var evt = _pendingOccurrences[eventIndex];
            var choice = choiceIndex == 0 ? evt.ChoiceA : evt.ChoiceB;

            RandomOccurrenceOutcomeApplier.Apply(_ledger, choice.Effects);
            _pendingOccurrences.RemoveAt(eventIndex);
            return true;
        }

        public int PendingCount => _pendingOccurrences.Count;

        // --- Save/Load ---
        public RandomOccurrenceStateDto ToDto()
        {
            var dto = new RandomOccurrenceStateDto
            {
                LastSpawnCheckUtc = _lastSpawnCheckUtc.ToString("O"),
                PendingOccurrenceIds = new List<string>()
            };
            foreach (var e in _pendingOccurrences)
                dto.PendingOccurrenceIds.Add(e.OccurrenceId);
            return dto;
        }

        public void LoadFromDto(RandomOccurrenceStateDto dto)
        {
            if (dto == null) return;

            _lastSpawnCheckUtc = DateTime.TryParse(dto.LastSpawnCheckUtc, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed : _clock.UtcNow;

            _pendingOccurrences.Clear();
            if (dto.PendingOccurrenceIds != null)
            {
                var lookup = new Dictionary<string, RandomOccurrenceDefinition>();
                foreach (var e in _occurrencePool)
                    lookup[e.OccurrenceId] = e;

                foreach (var id in dto.PendingOccurrenceIds)
                {
                    if (lookup.TryGetValue(id, out var evt))
                        _pendingOccurrences.Add(evt);
                }
            }

            ProcessSpawning();
        }
    }
}
