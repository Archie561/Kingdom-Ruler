using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using VContainer;
using KingdomRuler.Systems.Clock;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Systems.Save;

namespace KingdomRuler.Modules.Trade
{
    /// <summary>
    /// The Trade mechanic's entry point (GDD §7): its lifecycle, its clock, and every Ledger
    /// transaction it makes. The only class in the module that touches the Ledger or the clock.
    /// </summary>
    /// <remarks>
    /// <para><b>Prices sit beside the charge.</b> Each purchase has its price, its affordability
    /// check and its <c>Try…</c> side by side, and every <c>Try…</c> re-reads the price at the moment
    /// it charges, so the price shown is the price taken.</para>
    ///
    /// <para><b>Whoever decides from a trade amount, or changes a capacity, settles regeneration
    /// first</b> (<c>ARCHITECTURE.md</c> §4.5). The <c>Try…</c> methods and <see cref="AcceptOffer"/>
    /// call <c>AccruePassiveResourceRegen</c>, never <see cref="AdvanceTo"/>, so the offer list can
    /// never be refreshed in the middle of a transaction.</para>
    ///
    /// <para><b>The warehouse level is the stored fact</b>; capacity is derived from it through
    /// <see cref="WarehouseCurve"/> and handed to the Ledger, which derives regeneration from it.</para>
    /// </remarks>
    public sealed class TradeManager
    {
        private static readonly TradeResourceType[] AllResources =
            (TradeResourceType[])Enum.GetValues(typeof(TradeResourceType));

        private readonly KingdomLedger       _ledger;
        private readonly IClock              _clock;
        private readonly TradeConfig         _config;
        private readonly TradeOfferGenerator _generator;
        private readonly OfferRefreshTimer   _refreshTimer = new();

        private readonly List<TradeOffer> _offers = new();
        private readonly Dictionary<TradeResourceType, int> _levels = new();

        /// <summary>The offer list changed: replaced by a refresh, or shortened by an accept. Raised once per change.</summary>
        public event Action OffersChanged;

        [Inject]
        public TradeManager(KingdomLedger ledger, IClock clock, TradeConfig config)
            : this(ledger, clock, config, new System.Random()) { }

        /// <summary>Takes a seeded RNG so tests can reproduce a batch.</summary>
        public TradeManager(KingdomLedger ledger, IClock clock, TradeConfig config, System.Random rng)
        {
            _ledger    = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _clock     = clock  ?? throw new ArgumentNullException(nameof(clock));
            if (config == null) throw new ArgumentNullException(nameof(config));   // a UnityEngine.Object: not `??`
            _config    = config;
            _generator = new TradeOfferGenerator(rng ?? throw new ArgumentNullException(nameof(rng)));

            foreach (var resource in AllResources) _levels[resource] = 0;

            // The manager and the clock are root-scope singletons, so this is never unsubscribed.
            _clock.Ticked += AdvanceTo;
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        /// <summary>A fresh game: base warehouses, a regen baseline at now, and a first batch of offers.</summary>
        public void InitializeNewGame()
        {
            var now = _clock.UtcNow;
            foreach (var resource in AllResources)
            {
                _levels[resource] = 0;
                ApplyCapacity(resource);
            }

            // The Ledger's states start their baseline from the system clock; the injected one is the truth.
            _ledger.ResetRegenBaseline(now);

            RefreshOffers();
            _refreshTimer.Start(now, _config.OfferRefreshTimeSeconds);
            OffersChanged?.Invoke();
        }

        /// <summary>Restore a save, then settle the time the app was closed. Null starts a fresh game.</summary>
        /// <remarks>
        /// Order matters: levels — and so capacities — are restored before any offer is generated
        /// or any regen settles, so neither ever runs against a capacity that is about to change.
        /// </remarks>
        public void LoadFromDto(TradeStateDto dto)
        {
            if (dto == null)
            {
                InitializeNewGame();
                return;
            }

            var now = _clock.UtcNow;

            RestoreLevels(dto.WarehouseLevels);
            RestoreOffers(dto.ActiveOffers);
            _refreshTimer.Restore(ParseUtc(dto.NextOfferRefreshDueUtc), now, _config.OfferRefreshTimeSeconds);

            if (_offers.Count == 0) RefreshOffers();

            _ledger.AccruePassiveResourceRegen(now);
            if (_refreshTimer.Advance(now, _config.OfferRefreshTimeSeconds)) RefreshOffers();

            OffersChanged?.Invoke();
        }

        public TradeStateDto ToDto()
        {
            var dto = new TradeStateDto
            {
                NextOfferRefreshDueUtc = _refreshTimer.NextDueUtc?.ToString("O"),
                WarehouseLevels        = new Dictionary<string, int>(),
                ActiveOffers           = new List<TradeOfferDto>(_offers.Count)
            };

            foreach (var pair in _levels)  dto.WarehouseLevels[pair.Key.ToString()] = pair.Value;
            foreach (var offer in _offers) dto.ActiveOffers.Add(ToDto(offer));
            return dto;
        }

        /// <summary>Settle the offer refresh up to <paramref name="now"/>. Runs on every clock tick; cheap when nothing is due.</summary>
        public void AdvanceTo(DateTime now)
        {
            if (!_refreshTimer.Advance(now, _config.OfferRefreshTimeSeconds)) return;

            RefreshOffers();
            OffersChanged?.Invoke();
        }

        // ── Offers ────────────────────────────────────────────────────────────────

        public IReadOnlyList<TradeOffer> Offers => _offers;

        /// <summary>When the list is next replaced on its own. Every refresh moves it; an accept never does.</summary>
        public DateTime? NextRefreshDueUtc => _refreshTimer.NextDueUtc;

        public float SecondsUntilRefresh => _refreshTimer.SecondsUntilNext(_clock.UtcNow);

        public bool TryGetOffer(string offerId, out TradeOffer offer)
        {
            offer = FindOffer(offerId);
            return offer != null;
        }

        /// <summary>Every issue with an offer right now — what the player is short of, what will not fit — changing nothing.</summary>
        public TradeOfferEvaluation Evaluate(string offerId)
        {
            var offer = FindOffer(offerId);
            if (offer == null) return TradeOfferEvaluation.Missing();

            var issues = new List<TradeIssue>();

            foreach (var amount in offer.Give)
            {
                int shortfall = amount.Units - _ledger.GetTradeResource(amount.Resource).Amount;
                if (shortfall > 0)
                    issues.Add(new TradeIssue(TradeIssueKind.InsufficientResource, amount.Resource, shortfall));
            }

            foreach (var amount in offer.Receive)
            {
                int excess = amount.Units - RoomIn(amount.Resource);
                if (excess > 0)
                    issues.Add(new TradeIssue(TradeIssueKind.WouldOverflowWarehouse, amount.Resource, excess));
            }

            return new TradeOfferEvaluation(true, issues);
        }

        /// <summary>
        /// Take an offer: spend the give side, add the receive side, remove the offer. Refused —
        /// spending nothing — only when the give side cannot be paid; a full warehouse loses the
        /// excess, which the result reports.
        /// </summary>
        public TradeAcceptResult AcceptOffer(string offerId)
        {
            // Settled first, so a player who crossed the threshold a moment ago is honoured.
            _ledger.AccruePassiveResourceRegen(_clock.UtcNow);

            var offer = FindOffer(offerId);
            if (offer == null) return TradeAcceptResult.Refused(new[] { TradeIssue.Unavailable() });

            var evaluation = Evaluate(offerId);
            if (!evaluation.CanAccept) return TradeAcceptResult.Refused(evaluation.Issues);

            foreach (var amount in offer.Give)
            {
                if (_ledger.SpendTradeResource(amount.Resource, amount.Units)) continue;

                // Evaluate just found this affordable, so this is a broken invariant, not a player case.
                Debug.LogError($"[TradeManager] Offer '{offerId}' passed evaluation but spending {amount} failed; " +
                               "the trade is partially applied.");
            }

            var received  = new List<TradeAmount>(offer.Receive.Count);
            var forfeited = new List<TradeAmount>();
            foreach (var amount in offer.Receive)
            {
                int fitted = _ledger.AddTradeResource(amount.Resource, amount.Units);
                received.Add(new TradeAmount(amount.Resource, fitted));
                if (fitted < amount.Units) forfeited.Add(new TradeAmount(amount.Resource, amount.Units - fitted));
            }

            _offers.Remove(offer);
            OffersChanged?.Invoke();
            return TradeAcceptResult.Success(received, forfeited, evaluation.Issues);
        }

        // ── Instant refresh ───────────────────────────────────────────────────────

        public int  InstantRefreshCost      => _config.InstantRefreshCrystalCost;
        public bool CanAffordInstantRefresh => _ledger.Crystals >= InstantRefreshCost;

        /// <summary>Replace the offer list now for crystals, restarting the refresh interval.</summary>
        public bool TryInstantRefresh()
        {
            if (!_ledger.SpendCrystals(InstantRefreshCost)) return false;

            RefreshOffers();
            _refreshTimer.Start(_clock.UtcNow, _config.OfferRefreshTimeSeconds);
            OffersChanged?.Invoke();
            return true;
        }

        // ── Warehouses ────────────────────────────────────────────────────────────

        /// <summary>The stored level: 0 is unupgraded. The screen shows it counting from 1.</summary>
        public int WarehouseLevel(TradeResourceType type) => _levels[type];

        public bool CanUpgradeFurther(TradeResourceType type) => _levels[type] < WarehouseCurve.TopLevel(_config);

        /// <summary>The capacity the next upgrade gives — the current one at the cap.</summary>
        public int CapacityAfterUpgrade(TradeResourceType type) =>
            WarehouseCurve.Capacity(_config, Math.Min(_levels[type] + 1, WarehouseCurve.TopLevel(_config)));

        public int CrystalUpgradeCost(TradeResourceType type) => WarehouseCurve.CrystalCost(_config, _levels[type]);

        public bool CanAffordCrystalUpgrade(TradeResourceType type) =>
            CanUpgradeFurther(type) && _ledger.Crystals >= CrystalUpgradeCost(type);

        public bool TryUpgradeWithCrystals(TradeResourceType type)
        {
            _ledger.AccruePassiveResourceRegen(_clock.UtcNow);
            if (!CanUpgradeFurther(type) || !_ledger.SpendCrystals(CrystalUpgradeCost(type))) return false;

            RaiseLevel(type);
            return true;
        }

        /// <summary>
        /// The paired price: 80% of the capacity of the warehouse <b>being upgraded</b>, rounded up,
        /// paid out of the <b>paired</b> resource's stock (GDD §7).
        /// </summary>
        /// <remarks>
        /// The paired stock can never exceed the paired capacity, so this path needs the pair to be
        /// at least 80% of this warehouse's size. With capacity growing ×1.5 a level, a warehouse can
        /// run at most one level ahead of its pair this way — the pair advances in step. Crystals
        /// are the way past that.
        /// </remarks>
        public int PairedUpgradeCost(TradeResourceType type)
        {
            long capacity = _ledger.GetTradeResource(type).Capacity;
            return (int)((capacity * 4 + 4) / 5);   // ceil(capacity × 4/5), exact
        }

        public bool CanAffordPairedUpgrade(TradeResourceType type) =>
            CanUpgradeFurther(type)
            && _ledger.HasTradeResource(TradeResourcePair.PairedWith(type), PairedUpgradeCost(type));

        public bool TryUpgradeWithPairedResource(TradeResourceType type)
        {
            _ledger.AccruePassiveResourceRegen(_clock.UtcNow);
            if (!CanUpgradeFurther(type)) return false;
            if (!_ledger.SpendTradeResource(TradeResourcePair.PairedWith(type), PairedUpgradeCost(type))) return false;

            RaiseLevel(type);
            return true;
        }

        // ── Regeneration read-outs (the warehouse popup) ──────────────────────────

        /// <summary>Progress toward the next unit, 0..1; 0 while full.</summary>
        public float NextUnitProgress(TradeResourceType type) =>
            _ledger.GetTradeResource(type).NextUnitProgress(_clock.UtcNow);

        public float SecondsPerUnit(TradeResourceType type) =>
            (float)_ledger.GetTradeResource(type).TimePerUnit.TotalSeconds;

        /// <summary>Zero when full.</summary>
        public float SecondsUntilFull(TradeResourceType type) =>
            (float)_ledger.GetTradeResource(type).TimeUntilFull(_clock.UtcNow).TotalSeconds;

        /// <summary>Capacity ÷ 24, rounded down: the whole units a warehouse gains in an hour.</summary>
        public int UnitsPerHour(TradeResourceType type) => _ledger.GetTradeResource(type).Capacity / 24;

        // ── Private ───────────────────────────────────────────────────────────────

        private int RoomIn(TradeResourceType type)
        {
            var state = _ledger.GetTradeResource(type);
            return Math.Max(0, state.Capacity - state.Amount);
        }

        private void ApplyCapacity(TradeResourceType type) =>
            _ledger.SetWarehouseCapacity(type, WarehouseCurve.Capacity(_config, _levels[type]));

        private void RaiseLevel(TradeResourceType type)
        {
            _levels[type]++;
            ApplyCapacity(type);
        }

        /// <summary>A new batch, sized from the capacities as they are now. Offers already listed are never resized.</summary>
        private void RefreshOffers()
        {
            var capacities = new Dictionary<TradeResourceType, int>(AllResources.Length);
            foreach (var resource in AllResources) capacities[resource] = _ledger.GetTradeResource(resource).Capacity;

            _offers.Clear();
            _offers.AddRange(_generator.GenerateBatch(_config.OfferCount, _config.OfferCapacityShare, capacities));
        }

        private TradeOffer FindOffer(string offerId)
        {
            if (string.IsNullOrEmpty(offerId)) return null;
            foreach (var offer in _offers)
                if (offer.Id == offerId) return offer;
            return null;
        }

        private void RestoreLevels(Dictionary<string, int> levels)
        {
            foreach (var resource in AllResources)
            {
                int level = 0;

                // Clamped, not trusted: the save is plain JSON on the device.
                if (levels != null && levels.TryGetValue(resource.ToString(), out int saved))
                    level = Math.Clamp(saved, 0, WarehouseCurve.TopLevel(_config));

                _levels[resource] = level;
                ApplyCapacity(resource);
            }
        }

        private void RestoreOffers(List<TradeOfferDto> saved)
        {
            _offers.Clear();
            if (saved == null) return;

            foreach (var dto in saved)
                if (TryFromDto(dto, out var offer)) _offers.Add(offer);
        }

        private static TradeOfferDto ToDto(TradeOffer offer)
        {
            var dto = new TradeOfferDto { Id = offer.Id, Profitability = offer.Profitability.ToString() };
            foreach (var amount in offer.Give)    dto.Give[amount.Resource.ToString()]    = amount.Units;
            foreach (var amount in offer.Receive) dto.Receive[amount.Resource.ToString()] = amount.Units;
            return dto;
        }

        /// <summary>
        /// Rebuild a saved offer. Unknown resources and amounts below 1 are skipped; an offer left
        /// with an empty side, or otherwise invalid, is dropped rather than failing the load.
        /// </summary>
        private static bool TryFromDto(TradeOfferDto dto, out TradeOffer offer)
        {
            offer = null;
            if (dto == null) return false;

            if (!TryParseName(dto.Profitability, out TradeProfitability profitability))
                profitability = TradeProfitability.Neutral;

            return TradeOffer.TryCreate(dto.Id, ParseAmounts(dto.Give), ParseAmounts(dto.Receive), profitability, out offer);
        }

        private static List<TradeAmount> ParseAmounts(Dictionary<string, int> saved)
        {
            var amounts = new List<TradeAmount>();
            if (saved == null) return amounts;

            foreach (var pair in saved)
                if (pair.Value >= 1 && TryParseName(pair.Key, out TradeResourceType resource))
                    amounts.Add(new TradeAmount(resource, pair.Value));
            return amounts;
        }

        /// <summary>By member name only: <c>Enum.TryParse</c> alone also accepts "3" or "99".</summary>
        private static bool TryParseName<T>(string name, out T value) where T : struct
        {
            value = default;
            return !string.IsNullOrEmpty(name)
                   && !char.IsDigit(name[0]) && name[0] != '-'
                   && Enum.TryParse(name, out value)
                   && Enum.IsDefined(typeof(T), value);
        }

        private static DateTime? ParseUtc(string iso) =>
            DateTime.TryParse(iso, null, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : (DateTime?)null;
    }
}
