using System;
using KingdomRuler.Shared.Accrual;

namespace KingdomRuler.Modules.Economy
{
    /// <summary>
    /// What the player holds of one business type: the units owned and the type's one storage
    /// (GDD §8). Pure — handed the time, never reading a clock — and owned by <see cref="EconomyManager"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>The storage fills like a warehouse.</b> It holds <c>StorageHours</c> of production and
    /// fills by <see cref="WholeUnitAccrual"/>, one whole coin at a time, so an empty storage is full
    /// after exactly <c>StorageHours</c> at any number of units. <see cref="StartedUtc"/> is when the
    /// coin now accruing started: the partial coin lives there, not in a fraction.</para>
    ///
    /// <para><b>Every mutation settles first</b> (<c>ARCHITECTURE.md</c> §4.5), so time already elapsed
    /// is credited at the rate it was earned at, and a coin in progress carries over a purchase.</para>
    /// </remarks>
    public sealed class BusinessHolding
    {
        private readonly EconomyConfig _config;

        public BusinessDefinition Business { get; }

        public int  UnitsOwned { get; private set; }
        public long Stored     { get; private set; }

        /// <summary>When the coin now accruing started. Equals the last settle whenever no partial coin is pending.</summary>
        public DateTime StartedUtc { get; private set; }

        public BusinessHolding(BusinessDefinition business, EconomyConfig config, DateTime now)
        {
            Business   = business ?? throw new ArgumentNullException(nameof(business));
            _config    = config   ?? throw new ArgumentNullException(nameof(config));
            StartedUtc = now;
        }

        public long GoldPerHour     => BusinessProduction.GoldPerHour(Business, UnitsOwned);
        public long StorageCapacity => BusinessProduction.StorageCapacity(Business, UnitsOwned, _config);

        /// <summary>The storage holds all it can. A type with no units has no storage, so it is never full.</summary>
        public bool IsFull => UnitsOwned > 0 && Stored >= StorageCapacity;

        /// <summary>Units that can still be bought before <see cref="EconomyConfig.MaxUnitsPerType"/>.</summary>
        public int Room => EconomyConfig.MaxUnitsPerType - UnitsOwned;

        /// <summary>Credit every whole coin produced by <paramref name="now"/>.</summary>
        public void Settle(DateTime now)
        {
            Stored = WholeUnitAccrual.Settle(Stored, StorageCapacity, StartedUtc, BusinessProduction.StoragePeriod(_config),
                                             now, out var startedUtc);
            StartedUtc = startedUtc;
        }

        /// <summary>Settle at the old rate, then add units: the rate and the capacity rise together.</summary>
        public void AddUnits(int count, DateTime now)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), count, "Buy at least one unit.");
            if (count > Room) throw new ArgumentOutOfRangeException(nameof(count), count, $"Only {Room} more can be owned.");

            Settle(now);
            UnitsOwned += count;
        }

        /// <summary>Settle, then take up to <paramref name="limit"/> from the storage. Returns what was taken; the rest stays.</summary>
        public long TakeStored(DateTime now, long limit)
        {
            Settle(now);
            long taken = Math.Min(Stored, Math.Max(0L, limit));
            Stored -= taken;
            return taken;
        }

        /// <summary>
        /// Replace the state with a saved one, clamped to what the current content allows. Settles
        /// nothing: the caller settles once every holding is restored.
        /// </summary>
        /// <remarks>
        /// Units are clamped to <c>0..MaxUnitsPerType</c> first, because capacity is derived from them
        /// through the current content; the stored gold is then clamped to that capacity, so a retuned
        /// smaller rate or a hand-edited file never holds more than the storage can.
        /// </remarks>
        public void Restore(int unitsOwned, long stored, DateTime startedUtc)
        {
            UnitsOwned = Math.Clamp(unitsOwned, 0, EconomyConfig.MaxUnitsPerType);
            Stored     = Math.Clamp(stored, 0L, StorageCapacity);
            StartedUtc = startedUtc;
        }
    }
}
