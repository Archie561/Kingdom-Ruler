using System;
using System.Collections.Generic;
using KingdomRuler.Systems.Events;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Systems.Save;
using KingdomRuler.Modules.Cities.Domain;

namespace KingdomRuler.Modules.Cities
{
    public sealed class CitiesManager
    {
        private readonly KingdomLedger _ledger;
        private readonly EventBus _eventBus;
        private readonly HashSet<string> _purchasedCityIds = new();
        private readonly Dictionary<string, RegionDefinition> _regions = new();
        private readonly Dictionary<string, CityDefinition> _cityLookup = new();

        public IReadOnlyCollection<string> PurchasedCityIds => _purchasedCityIds;
        public int TotalPopulation { get; private set; }

        public CitiesManager(KingdomLedger ledger, EventBus eventBus)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        }

        public void InitializeRegions(IEnumerable<RegionDefinition> regions)
        {
            _regions.Clear();
            _cityLookup.Clear();
            foreach (var region in regions)
            {
                if (region == null) continue;
                _regions[region.RegionId] = region;
                if (region.Cities != null)
                {
                    foreach (var city in region.Cities)
                    {
                        if (city != null)
                            _cityLookup[city.CityId] = city;
                    }
                }
            }
        }

        public bool IsCityPurchased(string cityId) => _purchasedCityIds.Contains(cityId);

        public (bool CanAfford, string FailReason) CheckCityAffordability(string cityId)
        {
            if (!_cityLookup.TryGetValue(cityId, out var city))
                return (false, "City not found");
            if (_purchasedCityIds.Contains(cityId))
                return (false, "Already purchased");
            return CityAffordabilityChecker.Check(
                _ledger, city.CharacteristicRequirements, city.TradeResourceCosts, city.GoldCost);
        }

        /// <summary>Purchase a city. Spends gold + trade resources. Characteristics are gates, not spent.</summary>
        public bool PurchaseCity(string cityId)
        {
            var (canAfford, _) = CheckCityAffordability(cityId);
            if (!canAfford) return false;

            var city = _cityLookup[cityId];

            // Spend gold
            if (city.GoldCost > 0 && !_ledger.SpendGold(city.GoldCost))
                return false;

            // Spend trade resources
            if (city.TradeResourceCosts != null)
            {
                foreach (var cost in city.TradeResourceCosts)
                    _ledger.SpendTradeResource(cost.Type, cost.Amount);
            }

            _purchasedCityIds.Add(cityId);
            TotalPopulation += city.PopulationGranted;
            return true;
        }

        /// <summary>Check if all cities in a region are purchased.</summary>
        public bool IsRegionComplete(string regionId)
        {
            if (!_regions.TryGetValue(regionId, out var region)) return false;
            if (region.Cities == null || region.Cities.Length == 0) return false;
            foreach (var city in region.Cities)
            {
                if (city != null && !_purchasedCityIds.Contains(city.CityId))
                    return false;
            }
            return true;
        }

        // --- Save/Load ---
        public CitiesStateDto ToDto()
        {
            return new CitiesStateDto
            {
                PurchasedCityIds = new List<string>(_purchasedCityIds),
                TotalPopulation = TotalPopulation
            };
        }

        public void LoadFromDto(CitiesStateDto dto)
        {
            if (dto == null) return;
            _purchasedCityIds.Clear();
            if (dto.PurchasedCityIds != null)
            {
                foreach (var id in dto.PurchasedCityIds)
                    _purchasedCityIds.Add(id);
            }
            TotalPopulation = dto.TotalPopulation;
        }
    }
}
