using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using KingdomRuler.Systems.Clock;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Systems.Save;

namespace KingdomRuler.Modules.Economy
{
    /// <summary>
    /// The Economy mechanic's entry point (GDD §8): its lifecycle, its clock, and every Ledger
    /// transaction it makes — buying units for gold, the crystal top-up, collecting. The only class in
    /// the module that touches the Ledger or the clock.
    /// </summary>
    /// <remarks>
    /// <para><b>Prices sit beside the charge.</b> Each purchase has its price, its affordability check
    /// and its <c>Try…</c> side by side, and every <c>Try…</c> re-reads the price at the moment it
    /// charges, so the price shown is the price taken.</para>
    ///
    /// <para><b>Whoever decides from a storage, or changes a rate, settles it first</b>
    /// (<c>ARCHITECTURE.md</c> §4.5): <see cref="BusinessHolding"/> settles inside every mutation, and
    /// each clock tick settles every storage, so a read is never more than a tick old.</para>
    ///
    /// <para>Businesses are addressed by <c>BusinessId</c>, the save key. An unknown id is never an
    /// exception: reads give zero and every <c>Try…</c> refuses.</para>
    /// </remarks>
    public sealed class EconomyManager
    {
        private readonly KingdomLedger _ledger;
        private readonly IClock        _clock;
        private readonly EconomyConfig _config;

        private readonly List<BusinessHolding> _holdings = new();
        private readonly Dictionary<string, BusinessHolding> _holdingById = new();
        private readonly List<BusinessDefinition> _businesses = new();

        /// <summary>
        /// One or more storages became full on this tick. Raised at most once per tick, however many
        /// filled together, so a burst on resume is one sound. Never raised at load.
        /// </summary>
        public event Action StoragesFilled;

        public EconomyManager(KingdomLedger ledger, IClock clock, EconomyConfig config)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _clock  = clock  ?? throw new ArgumentNullException(nameof(clock));
            if (config == null) throw new ArgumentNullException(nameof(config));   // a UnityEngine.Object: not `??`
            _config = config;

            BuildHoldings();

            // The manager and the clock are root-scope singletons, so this is never unsubscribed.
            _clock.Ticked += AdvanceTo;
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        /// <summary>Every business type, in display order.</summary>
        public IReadOnlyList<BusinessDefinition> Businesses => _businesses;

        /// <summary>What a new game starts with: exactly the cheapest business's price (GDD §8). Zero with no businesses.</summary>
        public long StartingGold
        {
            get
            {
                long cheapest = 0;
                foreach (var holding in _holdings)
                    if (cheapest == 0 || holding.Business.UnitPrice < cheapest) cheapest = holding.Business.UnitPrice;
                return cheapest;
            }
        }

        /// <summary>
        /// A fresh Economy: nothing owned, empty storages, and <see cref="StartingGold"/> added to the
        /// balance. Also how a save from before Economy existed gets its start.
        /// </summary>
        public void InitializeNewGame()
        {
            var now = _clock.UtcNow;
            foreach (var holding in _holdings) holding.Restore(0, 0, now);
            _ledger.AddGold(StartingGold);
        }

        /// <summary>Restore a save, then settle the time the app was closed. Null starts a fresh Economy.</summary>
        /// <remarks>
        /// Defensive, because the save is plain JSON on the device: an unknown id is skipped with a
        /// warning, a business the save does not mention owns nothing, every number is clamped by
        /// <see cref="BusinessHolding.Restore"/>, and a missing or unreadable timestamp becomes now —
        /// losing at most the coin in progress, never granting gold.
        /// </remarks>
        public void LoadFromDto(EconomyStateDto dto)
        {
            if (dto == null)
            {
                InitializeNewGame();
                return;
            }

            var now   = _clock.UtcNow;
            var saved = dto.Businesses ?? new Dictionary<string, BusinessStateDto>();

            foreach (var holding in _holdings)
            {
                if (saved.TryGetValue(holding.Business.BusinessId, out var business) && business != null)
                    holding.Restore(business.UnitsOwned, business.Stored, ParseUtcOr(business.LastUpdatedUtc, now));
                else
                    holding.Restore(0, 0, now);
            }

            foreach (var id in saved.Keys)
                if (!_holdingById.ContainsKey(id))
                    Debug.LogWarning($"[EconomyManager] The save holds business '{id}', which no longer exists; skipped.");

            // Offline accrual, once, with every rate final. A storage that fills here raises nothing:
            // the player was away, and the first tick finds it already full.
            foreach (var holding in _holdings) holding.Settle(now);
        }

        public EconomyStateDto ToDto()
        {
            var dto = new EconomyStateDto { Businesses = new Dictionary<string, BusinessStateDto>(_holdings.Count) };
            foreach (var holding in _holdings)
            {
                dto.Businesses[holding.Business.BusinessId] = new BusinessStateDto
                {
                    UnitsOwned     = holding.UnitsOwned,
                    Stored         = holding.Stored,
                    LastUpdatedUtc = holding.StartedUtc.ToString("O")
                };
            }
            return dto;
        }

        /// <summary>Settle every storage up to <paramref name="now"/>. Runs on every clock tick; publishes no Ledger event.</summary>
        public void AdvanceTo(DateTime now)
        {
            bool anyFilled = false;
            foreach (var holding in _holdings)
            {
                bool wasFull = holding.IsFull;
                holding.Settle(now);
                if (!wasFull && holding.IsFull) anyFilled = true;
            }

            if (anyFilled) StoragesFilled?.Invoke();
        }

        // ── Reads ─────────────────────────────────────────────────────────────────

        public int  UnitsOwned(string businessId)      => Find(businessId)?.UnitsOwned      ?? 0;
        public long GoldPerHour(string businessId)     => Find(businessId)?.GoldPerHour     ?? 0;
        public long StorageCapacity(string businessId) => Find(businessId)?.StorageCapacity ?? 0;

        /// <summary>Gold waiting in the storage, as of the last settle — never more than a tick old.</summary>
        public long Stored(string businessId) => Find(businessId)?.Stored ?? 0;

        public bool IsStorageFull(string businessId) => Find(businessId)?.IsFull ?? false;

        /// <summary>Units that can still be bought before <see cref="EconomyConfig.MaxUnitsPerType"/>; 0 for an unknown id.</summary>
        public int RoomFor(string businessId) => Find(businessId)?.Room ?? 0;

        // ── Buying for gold ───────────────────────────────────────────────────────

        /// <summary>The flat price of <paramref name="count"/> units.</summary>
        public long PriceOf(string businessId, int count)
        {
            var holding = Find(businessId);
            return holding == null ? 0 : BusinessPricing.PriceOf(holding.Business.UnitPrice, count);
        }

        public bool CanAfford(string businessId, int count) =>
            CanOwnMore(businessId, count) && _ledger.Gold >= PriceOf(businessId, count);

        /// <summary>"Max" (GDD §8): as many as the gold pays for, within the units ceiling. Zero when not one is affordable.</summary>
        public int MaxAffordable(string businessId)
        {
            var holding = Find(businessId);
            return holding == null ? 0 : BusinessPricing.AffordableCount(_ledger.Gold, holding.Business.UnitPrice, holding.Room);
        }

        /// <summary>
        /// Buy <paramref name="count"/> units for gold, unconfirmed (GDD §8). Refused — spending nothing —
        /// when the player is short, the count is not positive, or it passes the units ceiling.
        /// </summary>
        public bool TryBuy(string businessId, int count)
        {
            var holding = Find(businessId);
            if (holding == null || !CanOwnMore(businessId, count)) return false;
            if (!_ledger.SpendGold(PriceOf(businessId, count))) return false;

            holding.AddUnits(count, _clock.UtcNow);
            return true;
        }

        // ── Buying with a crystal top-up ──────────────────────────────────────────

        /// <summary>The gold the player is short of for <paramref name="count"/> units; 0 when they can pay.</summary>
        public long GoldShortfall(string businessId, int count) => Math.Max(0L, PriceOf(businessId, count) - _ledger.Gold);

        /// <summary>
        /// Crystals that cover <see cref="GoldShortfall"/>: <c>ceil(shortfall ÷ GoldPerCrystal)</c>, at
        /// least 1; 0 when nothing is missing. A <c>long</c>: a large shortfall can exceed any crystal balance.
        /// </summary>
        public long CrystalTopUpCost(string businessId, int count) =>
            BusinessPricing.CrystalsToCover(GoldShortfall(businessId, count), _config.GoldPerCrystal);

        public bool CanAffordCrystalTopUp(string businessId, int count) =>
            CanOwnMore(businessId, count)
            && GoldShortfall(businessId, count) > 0
            && CrystalTopUpCost(businessId, count) <= _ledger.Crystals;

        /// <summary>
        /// Buy <paramref name="count"/> units, covering the missing gold with crystals — one transaction,
        /// after the player confirms (GDD §8). Refused, changing nothing, when crystals are short, nothing
        /// is missing (the plain <see cref="TryBuy"/> applies), or the units ceiling would be passed.
        /// </summary>
        /// <remarks>
        /// Spends the crystals and the player's whole gold balance: exactly "add the missing gold, then
        /// pay the price", but as one gold change, so no subscriber sees the balance in between, and
        /// nothing can meet the gold ceiling on the way. Crystals go first because they are the part
        /// that can be refused.
        /// </remarks>
        public bool TryBuyWithCrystalTopUp(string businessId, int count)
        {
            var holding = Find(businessId);
            if (holding == null || !CanAffordCrystalTopUp(businessId, count)) return false;

            long crystals = CrystalTopUpCost(businessId, count);   // ≤ Crystals, an int, by the check above
            if (!_ledger.SpendCrystals((int)crystals)) return false;

            long gold = _ledger.Gold;
            if (gold > 0 && !_ledger.SpendGold(gold))
                Debug.LogError($"[EconomyManager] Spending the whole gold balance failed for '{businessId}'; " +
                               "the crystals are spent and the units are granted.");

            holding.AddUnits(count, _clock.UtcNow);
            return true;
        }

        // ── Collecting ────────────────────────────────────────────────────────────

        /// <summary>
        /// Empty one business type's storage into the balance (GDD §8). Returns the gold moved. What would
        /// pass the gold ceiling stays in the storage, so nothing is silently lost.
        /// </summary>
        public long Collect(string businessId)
        {
            var holding = Find(businessId);
            if (holding == null) return 0;

            long taken = holding.TakeStored(_clock.UtcNow, KingdomLedger.MaxGold - _ledger.Gold);
            return _ledger.AddGold(taken);
        }

        // ── Private ───────────────────────────────────────────────────────────────

        private BusinessHolding Find(string businessId) =>
            businessId != null && _holdingById.TryGetValue(businessId, out var holding) ? holding : null;

        private bool CanOwnMore(string businessId, int count)
        {
            var holding = Find(businessId);
            return holding != null && count > 0 && count <= holding.Room;
        }

        /// <summary>
        /// One holding per listed business. Empty slots and ids are skipped; a repeated id keeps the
        /// first. Both are content mistakes <c>EconomyContentTests</c> fails on, so here they only log.
        /// </summary>
        private void BuildHoldings()
        {
            var now = _clock.UtcNow;
            if (_config.Businesses == null) return;

            foreach (var business in _config.Businesses)
            {
                if (business == null || string.IsNullOrWhiteSpace(business.BusinessId))
                {
                    Debug.LogError("[EconomyManager] EconomyConfig lists an empty business or one with no BusinessId; skipped.");
                    continue;
                }
                if (_holdingById.ContainsKey(business.BusinessId))
                {
                    Debug.LogError($"[EconomyManager] BusinessId '{business.BusinessId}' is listed twice; the first is kept.");
                    continue;
                }

                var holding = new BusinessHolding(business, _config, now);
                _holdings.Add(holding);
                _holdingById.Add(business.BusinessId, holding);
                _businesses.Add(business);
            }
        }

        private static DateTime ParseUtcOr(string iso, DateTime fallback) =>
            DateTime.TryParse(iso, null, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : fallback;
    }
}
