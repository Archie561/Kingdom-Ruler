using System;
using System.Collections.Generic;
using KingdomRuler.Systems.Events;
using KingdomRuler.Systems.Clock;
using KingdomRuler.Systems.Save;
using UnityEngine;

namespace KingdomRuler.Systems.Ledger
{
    /// <summary>
    /// The single source of truth for gold, crystals, trade resources and characteristics. Every
    /// module reads and writes through this API; a bug here corrupts saves or the economy.
    /// </summary>
    /// <remarks>
    /// Levelling questions go through the Ledger (<see cref="PointsRequiredForLevel"/>,
    /// <see cref="PointsRemainingForNextLevel"/>) rather than to
    /// <see cref="CharacteristicLevelingCurve"/> directly, so it stays the one place that knows how
    /// a characteristic levels.
    /// </remarks>
    public sealed class KingdomLedger
    {
        /// <summary>An empty warehouse refills in 24 hours whatever its capacity (GDD §7), so regen = capacity ÷ this.</summary>
        private const float SecondsPerDay = 86400f;

        private const float DefaultWarehouseCapacity = 100f;

        private readonly EventBus _eventBus;

        private long _gold;
        private int _crystals;
        private readonly Dictionary<TradeResourceType, TradeResourceState> _tradeResources;
        private readonly Dictionary<CharacteristicType, CharacteristicState> _characteristics;

        public long Gold => _gold;
        public int Crystals => _crystals;

        public KingdomLedger(EventBus eventBus)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _gold = 0;
            _crystals = 0;

            _tradeResources = new Dictionary<TradeResourceType, TradeResourceState>();
            foreach (TradeResourceType type in Enum.GetValues(typeof(TradeResourceType)))
            {
                _tradeResources[type] = new TradeResourceState(DefaultWarehouseCapacity, DefaultWarehouseCapacity / SecondsPerDay);
            }

            _characteristics = new Dictionary<CharacteristicType, CharacteristicState>();
            foreach (CharacteristicType type in Enum.GetValues(typeof(CharacteristicType)))
            {
                _characteristics[type] = new CharacteristicState();
            }
        }

        // --- Gold ---

        public void AddGold(long amount)
        {
            if (amount < 0) throw new ArgumentException("Use SpendGold for negative changes.", nameof(amount));
            _gold += amount;
            _eventBus.Publish(new GoldChangedEvent(_gold, amount));
        }

        public bool SpendGold(long amount)
        {
            if (amount < 0) throw new ArgumentException("Amount must be non-negative.", nameof(amount));
            if (_gold < amount) return false;
            _gold -= amount;
            _eventBus.Publish(new GoldChangedEvent(_gold, -amount));
            return true;
        }

        /// <summary>Set gold directly (for save/load hydration only).</summary>
        public void SetGold(long amount)
        {
            if (amount < 0) throw new ArgumentException("Gold cannot be negative.", nameof(amount));
            _gold = amount;
        }

        // --- Crystals ---

        public void AddCrystals(int amount)
        {
            if (amount < 0) throw new ArgumentException("Use SpendCrystals for negative changes.", nameof(amount));
            _crystals += amount;
            _eventBus.Publish(new CrystalsChangedEvent(_crystals, amount));
        }

        public bool SpendCrystals(int amount)
        {
            if (amount < 0) throw new ArgumentException("Amount must be non-negative.", nameof(amount));
            if (_crystals < amount) return false;
            _crystals -= amount;
            _eventBus.Publish(new CrystalsChangedEvent(_crystals, -amount));
            return true;
        }

        /// <summary>Set crystals directly (for save/load hydration only).</summary>
        public void SetCrystals(int amount)
        {
            if (amount < 0) throw new ArgumentException("Crystals cannot be negative.", nameof(amount));
            _crystals = amount;
        }

        // --- Trade Resources ---

        public TradeResourceState GetTradeResource(TradeResourceType type)
        {
            return _tradeResources[type];
        }

        /// <summary>
        /// Add trade resource, capped at warehouse capacity.
        /// Returns actual amount added (may be less than requested if at/near capacity).
        /// </summary>
        public float AddTradeResource(TradeResourceType type, float amount)
        {
            if (IsRejectedAsNonFinite(amount, nameof(AddTradeResource))) return 0f;
            if (amount < 0) throw new ArgumentException("Use SpendTradeResource for negative changes.", nameof(amount));
            var state = _tradeResources[type];
            float spaceAvailable = state.Capacity - state.Amount;
            float actualAdded = Math.Min(amount, spaceAvailable);
            if (actualAdded <= 0f) return 0f;

            state.Amount += actualAdded;
            _eventBus.Publish(new TradeResourceChangedEvent(type, state.Amount, actualAdded));
            return actualAdded;
        }

        /// <summary>
        /// Spend trade resource. Returns false if insufficient.
        /// </summary>
        public bool SpendTradeResource(TradeResourceType type, float amount)
        {
            if (IsRejectedAsNonFinite(amount, nameof(SpendTradeResource))) return false;
            if (amount < 0) throw new ArgumentException("Amount must be non-negative.", nameof(amount));
            var state = _tradeResources[type];
            if (state.Amount < amount) return false;

            state.Amount -= amount;
            _eventBus.Publish(new TradeResourceChangedEvent(type, state.Amount, -amount));
            return true;
        }

        /// <summary>Check if the player has at least this much of a trade resource.</summary>
        public bool HasTradeResource(TradeResourceType type, float amount)
        {
            return _tradeResources[type].Amount >= amount;
        }

        /// <summary>Set warehouse capacity (for upgrades).</summary>
        /// <remarks>
        /// <para>Callers hand over a capacity, never a rate: GDD §7 fixes the <i>fill time</i> at 24
        /// hours, so the regen rate is derived here and nowhere else (<c>ARCHITECTURE.md</c> §4.3).</para>
        ///
        /// <para>The stored amount is clamped down with the capacity; otherwise a warehouse retuned
        /// smaller would hold more than it can, never regenerate, and read as a stuck bar.</para>
        /// </remarks>
        public void SetWarehouseCapacity(TradeResourceType type, float newCapacity)
        {
            if (IsRejectedAsNonFinite(newCapacity, nameof(SetWarehouseCapacity))) return;
            if (newCapacity <= 0) throw new ArgumentException("Capacity must be positive.", nameof(newCapacity));
            var state = _tradeResources[type];
            state.Capacity = newCapacity;
            if (state.Amount > newCapacity) state.Amount = newCapacity;
            state.RegenRatePerSecond = newCapacity / SecondsPerDay;
        }

        // --- Characteristics ---

        public CharacteristicState GetCharacteristic(CharacteristicType type)
        {
            return _characteristics[type];
        }

        /// <summary>Points needed to advance from <paramref name="level"/> to the next.</summary>
        public float PointsRequiredForLevel(int level) => CharacteristicLevelingCurve.PointsRequired(level);

        /// <summary>
        /// Points still needed for this characteristic to reach its next level.
        /// Never negative.
        /// </summary>
        public float PointsRemainingForNextLevel(CharacteristicType type)
        {
            var state = _characteristics[type];
            return Math.Max(0f, CharacteristicLevelingCurve.PointsRequired(state.Level) - state.PointsIntoCurrentLevel);
        }

        /// <summary>
        /// Add points to a characteristic, levelling it up as many times as they cover. The curve
        /// is deliberately not a parameter, so every mechanic levels a characteristic at one rate.
        /// </summary>
        public void AddCharacteristicPoints(CharacteristicType type, float points)
        {
            if (IsRejectedAsNonFinite(points, nameof(AddCharacteristicPoints))) return;
            if (points <= 0) return;
            var state = _characteristics[type];

            int startLevel = state.Level;
            state.PointsIntoCurrentLevel += points;

            // Terminates because CharacteristicLevelingCurve.PointsRequired is guaranteed positive.
            float required = CharacteristicLevelingCurve.PointsRequired(state.Level);
            while (state.PointsIntoCurrentLevel >= required)
            {
                state.PointsIntoCurrentLevel -= required;
                state.Level++;
                required = CharacteristicLevelingCurve.PointsRequired(state.Level);
            }

            // Publish only after the loop: the bus is synchronous, so a subscriber publishing from
            // inside it would render a level already bumped with its points not yet carried over.
            for (int level = startLevel + 1; level <= state.Level; level++)
                _eventBus.Publish(new CharacteristicLeveledUpEvent(type, level));
        }

        /// <summary>
        /// Remove progress within the current level, stopping at zero. A characteristic never drops
        /// below a level it has reached (GDD §6), so the floor is simply the current level.
        /// </summary>
        public void ReduceCharacteristicPoints(CharacteristicType type, float points)
        {
            if (IsRejectedAsNonFinite(points, nameof(ReduceCharacteristicPoints))) return;
            if (points <= 0) return;
            var state = _characteristics[type];
            state.PointsIntoCurrentLevel = Math.Max(0f, state.PointsIntoCurrentLevel - points);
        }

        /// <summary>Check if characteristic meets or exceeds required level.</summary>
        public bool MeetsCharacteristicLevel(CharacteristicType type, int requiredLevel)
        {
            return _characteristics[type].Level >= requiredLevel;
        }

        // --- Offline Accrual ---

        /// <summary>
        /// Settle passive regeneration of every trade resource up to <paramref name="now"/> (GDD §7).
        /// </summary>
        /// <remarks>
        /// <para><b>Publishes no <c>TradeResourceChangedEvent</c>, deliberately.</b> It runs on every
        /// clock tick, so publishing would put ~24 messages a second on the bus, forever, to announce
        /// tiny deltas — the idle battery drain GDD §3 forbids. Discrete changes go on the bus;
        /// continuous drift is polled by the views (<c>ARCHITECTURE.md</c> §4.5).</para>
        ///
        /// <para>Called on every clock tick (subscribed by <c>GameBootstrapper</c>; the Ledger holds no
        /// clock), on load once capacities are final, and by anything that decides from a trade
        /// amount or changes a capacity — first, so correctness never depends on when a tick landed.</para>
        /// </remarks>
        public void AccruePassiveResourceRegen(DateTime now)
        {
            foreach (var kvp in _tradeResources)
            {
                kvp.Value.AccruePassiveRegen(now);
            }
        }

        /// <summary>
        /// Start every warehouse's regen clock at <paramref name="now"/>, accruing nothing.
        /// </summary>
        /// <remarks>
        /// New game only; a load restores each resource's own timestamp. Needed because
        /// <see cref="TradeResourceState"/> seeds its baseline from the system clock at construction,
        /// not from the injected <c>IClock</c>.
        /// </remarks>
        public void ResetRegenBaseline(DateTime now)
        {
            foreach (var kvp in _tradeResources)
            {
                kvp.Value.LastUpdatedUtc = now;
            }
        }

        /// <summary>An immutable copy of the current amounts and levels.</summary>
        public LedgerSnapshot TakeSnapshot()
        {
            var resourceAmounts = new Dictionary<TradeResourceType, float>();
            foreach (var kvp in _tradeResources)
            {
                resourceAmounts[kvp.Key] = kvp.Value.Amount;
            }

            var characteristicLevels = new Dictionary<CharacteristicType, int>();
            foreach (var kvp in _characteristics)
            {
                characteristicLevels[kvp.Key] = kvp.Value.Level;
            }

            return new LedgerSnapshot(_gold, _crystals, resourceAmounts, characteristicLevels);
        }

        // --- Save / Load ---

        /// <summary>Serialize the whole ledger to a save DTO.</summary>
        public LedgerStateDto ToDto()
        {
            var dto = new LedgerStateDto { Gold = _gold, Crystals = _crystals };

            foreach (var kvp in _tradeResources)
            {
                dto.TradeResources[kvp.Key.ToString()] = new TradeResourceStateDto
                {
                    Amount = kvp.Value.Amount,
                    // A fallback only: Trade re-derives capacity from the upgrade level on load.
                    // The regen rate is not saved; it always follows from capacity.
                    Capacity       = kvp.Value.Capacity,
                    LastUpdatedUtc = kvp.Value.LastUpdatedUtc.ToString("O")
                };
            }

            foreach (var kvp in _characteristics)
            {
                dto.Characteristics[kvp.Key.ToString()] = new CharacteristicStateDto
                {
                    Level                  = kvp.Value.Level,
                    PointsIntoCurrentLevel = kvp.Value.PointsIntoCurrentLevel
                };
            }

            return dto;
        }

        /// <summary>
        /// Restore the ledger from a save DTO.
        /// </summary>
        /// <remarks>
        /// Every field is validated rather than trusted: the file is plain JSON on the device and
        /// may be hand-edited, truncated or from another build. Anything unreadable keeps the value
        /// the fresh ledger already holds, so a damaged save degrades to defaults rather than to a
        /// corrupt economy. Entries are keyed by enum <i>name</i>, so reordering an enum is safe;
        /// an unknown name is skipped.
        /// </remarks>
        public void LoadFromDto(LedgerStateDto dto)
        {
            if (dto == null) return;

            _gold     = Math.Max(0L, dto.Gold);
            _crystals = Math.Max(0, dto.Crystals);

            if (dto.TradeResources != null)
            {
                foreach (var kvp in dto.TradeResources)
                {
                    if (kvp.Value == null) continue;
                    if (!Enum.TryParse<TradeResourceType>(kvp.Key, out var type)) continue;
                    if (!_tradeResources.TryGetValue(type, out var state)) continue;

                    // A non-positive capacity would make the warehouse unusable and, via
                    // the 24h-fill rule, produce a zero regen rate. Keep the default.
                    if (kvp.Value.Capacity > 0f && float.IsFinite(kvp.Value.Capacity))
                        state.Capacity = kvp.Value.Capacity;

                    state.RegenRatePerSecond = state.Capacity / SecondsPerDay;

                    // Math.Clamp passes NaN straight through, so a non-finite amount is checked first.
                    state.Amount = float.IsFinite(kvp.Value.Amount)
                        ? Math.Clamp(kvp.Value.Amount, 0f, state.Capacity)
                        : 0f;

                    state.LastUpdatedUtc = ParseUtcOr(kvp.Value.LastUpdatedUtc, state.LastUpdatedUtc);
                }
            }

            if (dto.Characteristics == null) return;

            foreach (var kvp in dto.Characteristics)
            {
                if (kvp.Value == null) continue;
                if (!Enum.TryParse<CharacteristicType>(kvp.Key, out var type)) continue;
                if (!_characteristics.TryGetValue(type, out var state)) continue;

                // Levels are 1-based and the floor is permanent, so a zero or negative
                // level in the file is nonsense — never let it demote the player.
                state.Level = Math.Max(1, kvp.Value.Level);

                // Progress must sit inside the current level; anything else means the file
                // and the curve disagree, and carrying it would trigger a phantom level-up.
                float required = CharacteristicLevelingCurve.PointsRequired(state.Level);
                float points = kvp.Value.PointsIntoCurrentLevel;
                state.PointsIntoCurrentLevel = float.IsFinite(points)
                    ? Math.Clamp(points, 0f, Math.Max(0f, required - 0.001f))
                    : 0f;
            }
        }

        /// <summary>
        /// NaN or ±Infinity would loop the level-up forever or poison the save, so it is refused
        /// rather than thrown — a throw would abandon the caller's transaction halfway.
        /// </summary>
        private static bool IsRejectedAsNonFinite(float value, string operation)
        {
            if (float.IsFinite(value)) return false;
            Debug.LogError($"[KingdomLedger] {operation} refused a non-finite amount ({value}).");
            return true;
        }

        private static DateTime ParseUtcOr(string iso, DateTime fallback) =>
            DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : fallback;
    }
}
