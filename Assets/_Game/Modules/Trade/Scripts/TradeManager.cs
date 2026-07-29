using System;
using System.Collections.Generic;
using KingdomRuler.Core;
using KingdomRuler.Shared.Ledger;
using KingdomRuler.Shared.Services;
using KingdomRuler.Modules.Trade.Domain;

namespace KingdomRuler.Modules.Trade
{
    /// <summary>
    /// Model layer for the Trade mechanic. Manages trade offers,
    /// warehouse upgrades, and offer refresh timers.
    /// </summary>
    public sealed class TradeManager
    {
        private readonly KingdomLedger _ledger;
        private readonly EventBus _eventBus;
        private readonly IClock _clock;
        private readonly TradeConfig _config;
        private readonly TradeOfferGenerator _offerGenerator;

        private readonly List<TradeOffer> _activeOffers = new();
        private readonly Dictionary<TradeResourceType, int> _warehouseLevels = new();
        private DateTime _lastOfferRefreshUtc;

        public IReadOnlyList<TradeOffer> ActiveOffers => _activeOffers;
        public DateTime LastOfferRefreshUtc => _lastOfferRefreshUtc;

        public TradeManager(
            KingdomLedger ledger,
            EventBus eventBus,
            IClock clock,
            TradeConfig config)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _offerGenerator = new TradeOfferGenerator();
            _lastOfferRefreshUtc = _clock.UtcNow;

            // Initialize warehouse levels to 0 (base)
            foreach (TradeResourceType type in Enum.GetValues(typeof(TradeResourceType)))
            {
                _warehouseLevels[type] = 0;
            }

            // Generate initial offers
            RefreshOffers();
        }

        /// <summary>Get warehouse upgrade level for a resource.</summary>
        public int GetWarehouseLevel(TradeResourceType type) => _warehouseLevels[type];

        // --- Trade Offers ---

        /// <summary>
        /// Process offline accrual for offer refresh.
        /// If enough time has passed, generates new offers.
        /// </summary>
        public void ProcessOfferRefresh()
        {
            var now = _clock.UtcNow;
            var elapsed = (float)(now - _lastOfferRefreshUtc).TotalSeconds;

            if (elapsed >= _config.OfferRefreshTimeSeconds)
            {
                RefreshOffers();
                _lastOfferRefreshUtc = now;
            }
        }

        /// <summary>
        /// Instantly refresh offers by spending crystals.
        /// </summary>
        public bool InstantRefresh()
        {
            if (!_ledger.SpendCrystals(_config.InstantRefreshCrystalCost))
                return false;

            RefreshOffers();
            _lastOfferRefreshUtc = _clock.UtcNow;
            return true;
        }

        /// <summary>Seconds until next auto-refresh.</summary>
        public float GetSecondsUntilRefresh()
        {
            var elapsed = (float)(_clock.UtcNow - _lastOfferRefreshUtc).TotalSeconds;
            return Math.Max(0f, _config.OfferRefreshTimeSeconds - elapsed);
        }

        /// <summary>
        /// Accept a trade offer by index. Spends give resources, adds receive resources.
        /// Returns false if player lacks resources or receive would overflow.
        /// </summary>
        public bool AcceptOffer(int offerIndex)
        {
            if (offerIndex < 0 || offerIndex >= _activeOffers.Count)
                return false;

            var offer = _activeOffers[offerIndex];

            // Check player has enough give resources
            foreach (var kvp in offer.GiveResources)
            {
                if (!_ledger.HasTradeResource(kvp.Key, kvp.Value))
                    return false;
            }

            // Spend give resources
            foreach (var kvp in offer.GiveResources)
            {
                _ledger.SpendTradeResource(kvp.Key, kvp.Value);
            }

            // Add receive resources (capped at warehouse capacity)
            foreach (var kvp in offer.ReceiveResources)
            {
                _ledger.AddTradeResource(kvp.Key, kvp.Value);
            }

            _activeOffers.RemoveAt(offerIndex);
            return true;
        }

        // --- Warehouse Upgrades ---

        /// <summary>
        /// Upgrade warehouse via crystal path.
        /// </summary>
        public bool UpgradeWarehouseWithCrystals(TradeResourceType type)
        {
            int level = _warehouseLevels[type];
            int cost = WarehouseUpgradeCalculator.CrystalPathCost(level, _config.WarehouseCrystalCostCurve);

            if (!_ledger.SpendCrystals(cost))
                return false;

            _warehouseLevels[type] = level + 1;
            float newCapacity = WarehouseUpgradeCalculator.CapacityAtLevel(
                level + 1, _config.WarehouseCapacityCurve);
            _ledger.SetWarehouseCapacity(type, newCapacity);
            return true;
        }

        /// <summary>
        /// Upgrade warehouse via paired resource path.
        /// Costs 80% of the paired resource's current warehouse capacity.
        /// </summary>
        public bool UpgradeWarehouseWithResources(TradeResourceType type)
        {
            var pairedType = TradeResourcePair.GetPairedResource(type);
            var pairedState = _ledger.GetTradeResource(pairedType);
            float cost = WarehouseUpgradeCalculator.ResourcePathCost(pairedState.Capacity);

            if (!_ledger.SpendTradeResource(pairedType, cost))
                return false;

            int level = _warehouseLevels[type];
            _warehouseLevels[type] = level + 1;
            float newCapacity = WarehouseUpgradeCalculator.CapacityAtLevel(
                level + 1, _config.WarehouseCapacityCurve);
            _ledger.SetWarehouseCapacity(type, newCapacity);
            return true;
        }

        // --- Save/Load ---

        public TradeStateDto ToDto()
        {
            var dto = new TradeStateDto
            {
                LastOfferRefreshUtc = _lastOfferRefreshUtc.ToString("O"),
                WarehouseLevels = new Dictionary<string, int>()
            };

            foreach (var kvp in _warehouseLevels)
                dto.WarehouseLevels[kvp.Key.ToString()] = kvp.Value;

            return dto;
        }

        public void LoadFromDto(TradeStateDto dto)
        {
            if (dto == null) return;

            _lastOfferRefreshUtc = DateTime.TryParse(dto.LastOfferRefreshUtc, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : _clock.UtcNow;

            if (dto.WarehouseLevels != null)
            {
                foreach (var kvp in dto.WarehouseLevels)
                {
                    if (Enum.TryParse<TradeResourceType>(kvp.Key, out var type))
                    {
                        _warehouseLevels[type] = kvp.Value;
                        float capacity = WarehouseUpgradeCalculator.CapacityAtLevel(
                            kvp.Value, _config.WarehouseCapacityCurve);
                        _ledger.SetWarehouseCapacity(type, capacity);
                    }
                }
            }

            // Process any pending offer refresh
            ProcessOfferRefresh();
        }

        private void RefreshOffers()
        {
            _activeOffers.Clear();
            var newOffers = _offerGenerator.GenerateBatch(_config.OfferCount, _config.OfferBaseAmount);
            _activeOffers.AddRange(newOffers);
        }
    }
}
