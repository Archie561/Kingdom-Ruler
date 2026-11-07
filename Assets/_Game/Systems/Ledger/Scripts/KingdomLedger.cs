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
        private const int DefaultWarehouseCapacity = 100;

        /// <summary>
        /// The largest capacity a warehouse may have — far above play (Trade's top level holds
        /// ~333 thousand). It keeps whole-unit regeneration exact in <c>long</c> ticks
        /// (<see cref="TradeResourceState"/>), whatever a module's curve is tuned to.
        /// </summary>
        public const int MaxWarehouseCapacity = 10_000_000;

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
                _tradeResources[type] = new TradeResourceState(DefaultWarehouseCapacity);
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

        /// <summary>Add whole units, up to the warehouse's capacity. Returns how many fitted.</summary>
        public int AddTradeResource(TradeResourceType type, int amount)
        {
            if (amount < 0) throw new ArgumentException("Use SpendTradeResource for negative changes.", nameof(amount));
            var state = _tradeResources[type];
            int added = Math.Min(amount, state.Capacity - state.Amount);
            if (added <= 0) return 0;

            state.Amount += added;
            _eventBus.Publish(new TradeResourceChangedEvent(type, state.Amount, added));
            return added;
        }

        /// <summary>Spend whole units. Returns false, spending nothing, when there are not enough.</summary>
        public bool SpendTradeResource(TradeResourceType type, int amount)
        {
            if (amount < 0) throw new ArgumentException("Amount must be non-negative.", nameof(amount));
            var state = _tradeResources[type];
            if (state.Amount < amount) return false;

            state.Amount -= amount;
            _eventBus.Publish(new TradeResourceChangedEvent(type, state.Amount, -amount));
            return true;
        }

        public bool HasTradeResource(TradeResourceType type, int amount) => _tradeResources[type].Amount >= amount;

        /// <summary>Set a warehouse's capacity (for upgrades).</summary>
        /// <remarks>
        /// <para>Callers hand over a capacity, never a rate: GDD §7 fixes the <i>fill time</i> at 24
        /// hours, so regeneration follows from capacity here and nowhere else (<c>ARCHITECTURE.md</c> §4.3).
        /// Settle regen before calling, so the time already elapsed counts at the old rate.</para>
        ///
        /// <para>The stored amount is clamped down with the capacity; otherwise a warehouse retuned
        /// smaller would hold more than it can and never regenerate. A capacity above
        /// <see cref="MaxWarehouseCapacity"/> is a tuning mistake: logged, and clamped.</para>
        /// </remarks>
        public void SetWarehouseCapacity(TradeResourceType type, int newCapacity)
        {
            if (newCapacity <= 0) throw new ArgumentException("Capacity must be positive.", nameof(newCapacity));
            if (newCapacity > MaxWarehouseCapacity)
            {
                Debug.LogError($"[KingdomLedger] {type} capacity {newCapacity} is above the {MaxWarehouseCapacity} ceiling; clamped.");
                newCapacity = MaxWarehouseCapacity;
            }

            var state = _tradeResources[type];
            state.Capacity = newCapacity;
            if (state.Amount > newCapacity) state.Amount = newCapacity;
        }

        // --- Characteristics ---

        public CharacteristicState GetCharacteristic(CharacteristicType type)
        {
            return _characteristics[type];
        }

        /// <summary>Points needed to advance from <paramref name="level"/> to the next.</summary>
        public int PointsRequiredForLevel(int level) => CharacteristicLevelingCurve.PointsRequired(level);

        /// <summary>
        /// Points still needed for this characteristic to reach its next level.
        /// Never negative.
        /// </summary>
        public int PointsRemainingForNextLevel(CharacteristicType type)
        {
            var state = _characteristics[type];
            return Math.Max(0, CharacteristicLevelingCurve.PointsRequired(state.Level) - state.PointsIntoCurrentLevel);
        }

        /// <summary>
        /// Add points to a characteristic, levelling it up as many times as they cover. The curve
        /// is deliberately not a parameter, so every mechanic levels a characteristic at one rate.
        /// </summary>
        public void AddCharacteristicPoints(CharacteristicType type, int points)
        {
            if (points <= 0) return;
            var state = _characteristics[type];

            int startLevel = state.Level;

            // Summed as long, so a large award cannot overflow before the loop consumes it. After the
            // loop the remainder is below one level's requirement, which always fits an int.
            long total = (long)state.PointsIntoCurrentLevel + points;

            // Terminates because CharacteristicLevelingCurve.PointsRequired is guaranteed positive.
            int required = CharacteristicLevelingCurve.PointsRequired(state.Level);
            while (total >= required)
            {
                total -= required;
                state.Level++;
                required = CharacteristicLevelingCurve.PointsRequired(state.Level);
            }
            state.PointsIntoCurrentLevel = (int)total;

            // Publish only after the loop: the bus is synchronous, so a subscriber publishing from
            // inside it would render a level already bumped with its points not yet carried over.
            for (int level = startLevel + 1; level <= state.Level; level++)
                _eventBus.Publish(new CharacteristicLeveledUpEvent(type, level));
        }

        /// <summary>
        /// Remove progress within the current level, stopping at zero. A characteristic never drops
        /// below a level it has reached (GDD §6), so the floor is simply the current level.
        /// </summary>
        public void ReduceCharacteristicPoints(CharacteristicType type, int points)
        {
            if (points <= 0) return;
            var state = _characteristics[type];
            state.PointsIntoCurrentLevel = Math.Max(0, state.PointsIntoCurrentLevel - points);
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
        /// clock tick, and at high capacities a unit lands in every warehouse on most ticks, so
        /// publishing would put up to ~24 messages a second on the bus, forever — the idle battery
        /// drain GDD §3 forbids. Discrete changes (trades, upgrades, purchases) go on the bus;
        /// regeneration is polled by the views (<c>ARCHITECTURE.md</c> §4.5).</para>
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
            var resourceAmounts = new Dictionary<TradeResourceType, int>();
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
                    // Regen needs nothing else: its progress is in the timestamp.
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

                    // A non-positive capacity would be a warehouse that never refills: keep the default.
                    // (WholeNumberJsonConverter has already turned a NaN or a decimal into a whole number.)
                    if (kvp.Value.Capacity > 0)
                        state.Capacity = Math.Min(kvp.Value.Capacity, MaxWarehouseCapacity);

                    state.Amount = Math.Clamp(kvp.Value.Amount, 0, state.Capacity);

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
                int required = CharacteristicLevelingCurve.PointsRequired(state.Level);
                state.PointsIntoCurrentLevel = Math.Clamp(kvp.Value.PointsIntoCurrentLevel, 0, required - 1);
            }
        }

        private static DateTime ParseUtcOr(string iso, DateTime fallback) =>
            DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : fallback;
    }
}
