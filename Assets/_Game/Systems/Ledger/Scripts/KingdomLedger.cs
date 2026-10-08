using System;
using System.Collections.Generic;
using KingdomRuler.Systems.Events;
using KingdomRuler.Systems.Clock;
using KingdomRuler.Systems.Save;

namespace KingdomRuler.Systems.Ledger
{
    /// <summary>
    /// Single source of truth for all mutable game resource state.
    /// Every module reads and writes through this one API.
    /// PRIORITY #1: A bug here is save-corrupting or economy-breaking.
    /// </summary>
    public sealed class KingdomLedger
    {
        /// <summary>
        /// GDD §7: an empty warehouse refills to full in 24 hours, whatever its capacity —
        /// so the regen rate is always capacity ÷ this.
        /// </summary>
        private const float SecondsPerDay = 86400f;

        private const float DefaultWarehouseCapacity = 100f;

        private readonly EventBus _eventBus;

        private long _gold;
        private int _crystals;
        private readonly Dictionary<TradeResourceType, TradeResourceState> _tradeResources;
        private readonly Dictionary<CharacteristicType, CharacteristicState> _characteristics;

        public long Gold => _gold;
        public int Crystals => _crystals;

        // Callers ask the Ledger about levelling (PointsRequiredForLevel /
        // PointsRemainingForNextLevel) rather than reading CharacteristicLevelingCurve themselves:
        // the Ledger stays the one place that knows how a characteristic levels.

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
        /// <para>The regen rate is recomputed here and nowhere else. GDD §7 fixes the <i>fill
        /// time</i> at 24 hours rather than the rate, so a bigger warehouse must produce more per
        /// hour to still fill in a day. This derivation is Ledger-owned
        /// (<c>ARCHITECTURE.md</c> §4.3): callers hand over a capacity, never a rate.</para>
        ///
        /// <para>The stored amount is clamped down with the capacity. A designer retuning the
        /// curve downwards would otherwise leave a warehouse holding more than it can hold, which
        /// then never regenerates (it is already "full") and reads as a stuck bar.</para>
        /// </remarks>
        public void SetWarehouseCapacity(TradeResourceType type, float newCapacity)
        {
            if (newCapacity <= 0) throw new ArgumentException("Capacity must be positive.", nameof(newCapacity));
            var state = _tradeResources[type];
            state.Capacity = newCapacity;
            if (state.Amount > newCapacity) state.Amount = newCapacity;
            // Recalculate regen rate: fill time stays at 24h
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
        /// Add points to a characteristic. May trigger level-up(s).
        /// Thresholds come from the Ledger's own curve — deliberately not a parameter,
        /// so every mechanic levels a characteristic at the identical rate.
        /// </summary>
        public void AddCharacteristicPoints(CharacteristicType type, float points)
        {
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

            // Publish only once the mutation has fully settled. EventBus.Publish is
            // synchronous, so a subscriber runs inside this call — publishing from inside
            // the loop above let a Presenter observe (and render) a half-applied state,
            // with the level already bumped but the remaining points not yet carried over.
            for (int level = startLevel + 1; level <= state.Level; level++)
                _eventBus.Publish(new CharacteristicLeveledUpEvent(type, level));
        }

        /// <summary>
        /// Reduce points within the current level (from negative card effects).
        /// </summary>
        /// <remarks>
        /// Per GDD §6 a characteristic never drops below a level it has reached, so this
        /// only ever eats into progress within the current level and stops at zero — the
        /// level itself is never decremented. There is deliberately no separate "permanent
        /// floor" to track: the floor is always exactly the current level, so storing it
        /// would be storing a copy of <see cref="CharacteristicState.Level"/>.
        /// </remarks>
        public void ReduceCharacteristicPoints(CharacteristicType type, float points)
        {
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
        /// Fast-forward passive resource regeneration for all trade resources (GDD §7 — an empty
        /// warehouse refills over 24 hours, scaled to its capacity).
        /// </summary>
        /// <remarks>
        /// <para><b>Deliberately publishes no <c>TradeResourceChangedEvent</c>, and this is not an
        /// oversight.</b> It runs on every game-clock tick, four times a second, so
        /// publishing would put six messages per tick — around 24 a second, forever, on every
        /// screen — onto the shared bus to announce a delta of roughly a third of a thousandth of
        /// a warehouse. That is exactly the idle battery drain GDD §3 forbids in a game people
        /// leave installed.</para>
        ///
        /// <para>The seam is: <b>discrete changes go on the bus; continuous drift is polled.</b>
        /// Accepting an offer, upgrading a warehouse and a Cities purchase all publish normally
        /// through <see cref="AddTradeResource"/> / <see cref="SpendTradeResource"/>. The
        /// warehouse bars read the amount each frame and redraw only when the displayed value
        /// changes, the same way the Laws countdown is rendered (<c>ARCHITECTURE.md</c> §4.5).</para>
        ///
        /// <para>Takes <paramref name="now"/> rather than reading a clock: the Ledger holds no
        /// <c>IClock</c>, so something outside it always decides when time has passed. That is
        /// what keeps this directly unit-testable.</para>
        ///
        /// <para>Who calls it: during play, the game clock — <c>GameBootstrapper</c> subscribes
        /// this method to <c>IClock.Ticked</c>. On load, <c>TradeManager.LoadFromDto</c>, the
        /// first moment warehouse capacities are final. And <b>anything that decides from a trade
        /// amount, or changes a capacity, calls it first</b> — accepting an offer, upgrading a
        /// warehouse — so correctness never depends on whether a tick happened to land.</para>
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
        /// For a <b>new game only</b> — loading a save restores each resource's own timestamp,
        /// which is what makes offline accrual work. This exists because
        /// <see cref="TradeResourceState"/> seeds its baseline from the system clock at
        /// construction, which is not the injected <c>IClock</c>: without this, a fresh game's
        /// first accrual measures from whenever the object happened to be built rather than from
        /// when the game actually started.
        /// </remarks>
        public void ResetRegenBaseline(DateTime now)
        {
            foreach (var kvp in _tradeResources)
            {
                kvp.Value.LastUpdatedUtc = now;
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
                    // Capacity is a bootstrap value here; Trade re-asserts it from the upgrade
                    // level on load. The regen rate is not persisted at all — it is always
                    // capacity ÷ 86400 (schema v5).
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
        /// Every field is validated rather than trusted. This is the highest-consequence
        /// read in the game: the save file is plain JSON on the device, so its contents are
        /// whatever happens to be on disk — hand-edited, truncated by a crash mid-write, or
        /// written by an older build. Anything unreadable falls back to the value the
        /// freshly-constructed ledger already holds, so a partial save degrades to partial
        /// defaults instead of a corrupt economy or a divide-by-zero regen rate.
        ///
        /// Entries are keyed by enum *name*, so reordering the enums is safe; an unknown
        /// name (a resource that existed in an older build) is skipped.
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
                    if (kvp.Value.Capacity > 0f)
                        state.Capacity = kvp.Value.Capacity;

                    // Always derived, never read from the file (schema v5 dropped the field):
                    // GDD §7 fixes the fill time at 24h, so the rate follows from capacity.
                    state.RegenRatePerSecond = state.Capacity / SecondsPerDay;

                    state.Amount = Math.Clamp(kvp.Value.Amount, 0f, state.Capacity);

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
                state.PointsIntoCurrentLevel =
                    Math.Clamp(kvp.Value.PointsIntoCurrentLevel, 0f, Math.Max(0f, required - 0.001f));
            }
        }

        private static DateTime ParseUtcOr(string iso, DateTime fallback) =>
            DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : fallback;
    }
}
