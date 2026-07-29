using System;
using System.Collections.Generic;
using KingdomRuler.Core;

namespace KingdomRuler.Shared.Ledger
{
    /// <summary>
    /// Single source of truth for all mutable game resource state.
    /// Every module reads and writes through this one API.
    /// PRIORITY #1: A bug here is save-corrupting or economy-breaking.
    /// </summary>
    public sealed class KingdomLedger
    {
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
                _tradeResources[type] = new TradeResourceState(capacity: 100f, regenRatePerSecond: 100f / 86400f);
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
            _eventBus.Publish(new GoldChanged(_gold, amount));
        }

        public bool SpendGold(long amount)
        {
            if (amount < 0) throw new ArgumentException("Amount must be non-negative.", nameof(amount));
            if (_gold < amount) return false;
            _gold -= amount;
            _eventBus.Publish(new GoldChanged(_gold, -amount));
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
            _eventBus.Publish(new CrystalsChanged(_crystals, amount));
        }

        public bool SpendCrystals(int amount)
        {
            if (amount < 0) throw new ArgumentException("Amount must be non-negative.", nameof(amount));
            if (_crystals < amount) return false;
            _crystals -= amount;
            _eventBus.Publish(new CrystalsChanged(_crystals, -amount));
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
            if (amount < 0) throw new ArgumentException("Use SpendTradeResource for negative changes.", nameof(amount));
            var state = _tradeResources[type];
            float spaceAvailable = state.Capacity - state.Amount;
            float actualAdded = Math.Min(amount, spaceAvailable);
            if (actualAdded <= 0f) return 0f;

            state.Amount += actualAdded;
            _eventBus.Publish(new ResourceChanged(type, state.Amount, actualAdded));
            return actualAdded;
        }

        /// <summary>
        /// Spend trade resource. Returns false if insufficient.
        /// </summary>
        public bool SpendTradeResource(TradeResourceType type, float amount)
        {
            if (amount < 0) throw new ArgumentException("Amount must be non-negative.", nameof(amount));
            var state = _tradeResources[type];
            if (state.Amount < amount) return false;

            state.Amount -= amount;
            _eventBus.Publish(new ResourceChanged(type, state.Amount, -amount));
            return true;
        }

        /// <summary>Check if the player has at least this much of a trade resource.</summary>
        public bool HasTradeResource(TradeResourceType type, float amount)
        {
            return _tradeResources[type].Amount >= amount;
        }

        /// <summary>Set warehouse capacity (for upgrades).</summary>
        public void SetWarehouseCapacity(TradeResourceType type, float newCapacity)
        {
            if (newCapacity <= 0) throw new ArgumentException("Capacity must be positive.", nameof(newCapacity));
            var state = _tradeResources[type];
            state.Capacity = newCapacity;
            // Recalculate regen rate: fill time stays at 24h
            state.RegenRatePerSecond = newCapacity / 86400f;
        }

        // --- Characteristics ---

        public CharacteristicState GetCharacteristic(CharacteristicType type)
        {
            return _characteristics[type];
        }

        /// <summary>
        /// Add points to a characteristic. May trigger level-up(s).
        /// Uses the provided pointsRequiredFunc to determine level thresholds.
        /// </summary>
        public void AddCharacteristicPoints(CharacteristicType type, float points, Func<int, float> pointsRequiredFunc)
        {
            if (points <= 0) return;
            var state = _characteristics[type];
            state.PointsIntoCurrentLevel += points;

            // Check for level-ups
            float required = pointsRequiredFunc(state.Level);
            while (state.PointsIntoCurrentLevel >= required)
            {
                state.PointsIntoCurrentLevel -= required;
                state.Level++;
                state.PermanentFloor = Math.Max(state.PermanentFloor, state.Level);
                _eventBus.Publish(new CharacteristicLeveledUp(type, state.Level));
                required = pointsRequiredFunc(state.Level);
            }
        }

        /// <summary>
        /// Reduce points within current level (from negative card effects).
        /// Cannot drop below the permanent floor level.
        /// </summary>
        public void ReduceCharacteristicPoints(CharacteristicType type, float points, Func<int, float> pointsRequiredFunc)
        {
            if (points <= 0) return;
            var state = _characteristics[type];
            state.PointsIntoCurrentLevel -= points;

            // If points go negative, drop levels but never below floor
            while (state.PointsIntoCurrentLevel < 0 && state.Level > state.PermanentFloor)
            {
                state.Level--;
                float prevRequired = pointsRequiredFunc(state.Level);
                state.PointsIntoCurrentLevel += prevRequired;
            }

            // Clamp: if at floor and still negative, set to 0
            if (state.Level <= state.PermanentFloor && state.PointsIntoCurrentLevel < 0)
            {
                state.PointsIntoCurrentLevel = 0;
            }
        }

        /// <summary>Check if characteristic meets or exceeds required level.</summary>
        public bool MeetsCharacteristicLevel(CharacteristicType type, int requiredLevel)
        {
            return _characteristics[type].Level >= requiredLevel;
        }

        // --- Offline Accrual ---

        /// <summary>
        /// Fast-forward passive resource regeneration for all trade resources.
        /// Called on app resume / save load.
        /// </summary>
        public void AccruePassiveResourceRegen(DateTime now)
        {
            foreach (var kvp in _tradeResources)
            {
                kvp.Value.AccruePassiveRegen(now);
            }
        }

        /// <summary>
        /// Create an immutable snapshot of the current ledger state.
        /// Used for afford-checks and UI display to avoid mutation during a frame.
        /// </summary>
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
    }
}
