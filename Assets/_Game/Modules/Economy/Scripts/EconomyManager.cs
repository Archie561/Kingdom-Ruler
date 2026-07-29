using System;
using System.Collections.Generic;
using KingdomRuler.Core;
using KingdomRuler.Shared.Ledger;
using KingdomRuler.Shared.Services;
using KingdomRuler.Modules.Economy.Domain;

namespace KingdomRuler.Modules.Economy
{
    public sealed class EconomyManager
    {
        private readonly KingdomLedger _ledger;
        private readonly EventBus _eventBus;
        private readonly IClock _clock;
        private readonly EconomyConfig _config;

        // Runtime state per business type
        private readonly Dictionary<string, BusinessRuntimeState> _businessStates = new();
        private readonly Dictionary<string, BusinessDefinition> _definitions = new();

        public EconomyManager(KingdomLedger ledger, EventBus eventBus, IClock clock, EconomyConfig config)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public void InitializeBusinesses(IEnumerable<BusinessDefinition> allBusinesses)
        {
            _definitions.Clear();
            _businessStates.Clear();
            foreach (var biz in allBusinesses)
            {
                if (biz != null && !string.IsNullOrEmpty(biz.BusinessId))
                {
                    _definitions[biz.BusinessId] = biz;
                    if (!_businessStates.ContainsKey(biz.BusinessId))
                        _businessStates[biz.BusinessId] = new BusinessRuntimeState { LastAccruedUtc = _clock.UtcNow };
                }
            }
        }

        public int GetCountOwned(string businessId) =>
            _businessStates.TryGetValue(businessId, out var s) ? s.CountOwned : 0;

        public long GetStoredGold(string businessId) =>
            _businessStates.TryGetValue(businessId, out var s) ? s.StoredGold : 0;

        public long GetNextPurchaseCost(string businessId)
        {
            if (!_definitions.TryGetValue(businessId, out var def)) return long.MaxValue;
            int owned = GetCountOwned(businessId);
            return BusinessCostCalculator.CalculateCost(def.BaseCost, owned, def.CostMultiplier);
        }

        /// <summary>Buy one more unit of a business type.</summary>
        public bool BuyBusiness(string businessId)
        {
            if (!_definitions.TryGetValue(businessId, out var def)) return false;
            if (!_businessStates.TryGetValue(businessId, out var state)) return false;

            long cost = BusinessCostCalculator.CalculateCost(def.BaseCost, state.CountOwned, def.CostMultiplier);
            if (!_ledger.SpendGold(cost)) return false;

            // Accrue before changing count so we don't lose stored gold
            AccrueBusiness(businessId);
            state.CountOwned++;
            return true;
        }

        /// <summary>Collect stored gold from a business into the ledger.</summary>
        public long CollectGold(string businessId)
        {
            AccrueBusiness(businessId);
            if (!_businessStates.TryGetValue(businessId, out var state)) return 0;

            long collected = state.StoredGold;
            state.StoredGold = 0;
            state.LastAccruedUtc = _clock.UtcNow;
            if (collected > 0)
                _ledger.AddGold(collected);
            return collected;
        }

        /// <summary>Process offline accrual for all businesses.</summary>
        public void ProcessAccrual()
        {
            foreach (var kvp in _businessStates)
                AccrueBusiness(kvp.Key);
        }

        private void AccrueBusiness(string businessId)
        {
            if (!_definitions.TryGetValue(businessId, out var def)) return;
            if (!_businessStates.TryGetValue(businessId, out var state)) return;
            if (state.CountOwned <= 0) return;

            state.StoredGold = BusinessAccrualCalculator.Accrue(
                state.StoredGold, state.CountOwned, def.BaseGoldPerMinute,
                state.LastAccruedUtc, _clock.UtcNow);
            state.LastAccruedUtc = _clock.UtcNow;
        }

        // --- Save/Load ---
        public EconomyStateDto ToDto()
        {
            var dto = new EconomyStateDto { Businesses = new List<BusinessStateDto>() };
            foreach (var kvp in _businessStates)
            {
                dto.Businesses.Add(new BusinessStateDto
                {
                    BusinessId = kvp.Key,
                    CountOwned = kvp.Value.CountOwned,
                    StoredGold = kvp.Value.StoredGold,
                    LastAccruedUtc = kvp.Value.LastAccruedUtc.ToString("O")
                });
            }
            return dto;
        }

        public void LoadFromDto(EconomyStateDto dto)
        {
            if (dto?.Businesses == null) return;
            foreach (var biz in dto.Businesses)
            {
                if (_businessStates.TryGetValue(biz.BusinessId, out var state))
                {
                    state.CountOwned = biz.CountOwned;
                    state.StoredGold = biz.StoredGold;
                    state.LastAccruedUtc = DateTime.TryParse(biz.LastAccruedUtc, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                        ? parsed : _clock.UtcNow;
                }
            }
            ProcessAccrual();
        }

        private sealed class BusinessRuntimeState
        {
            public int CountOwned;
            public long StoredGold;
            public DateTime LastAccruedUtc;
        }
    }
}
