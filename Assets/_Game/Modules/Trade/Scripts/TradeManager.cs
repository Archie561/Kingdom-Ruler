using System;
using System.Collections.Generic;
using UnityEngine;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Systems.Clock;
using KingdomRuler.Systems.Save;
using KingdomRuler.Modules.Trade.Domain;

namespace KingdomRuler.Modules.Trade
{
    /// <summary>
    /// Model layer for the Trade mechanic (<c>GDD.md</c> §7): the offer list and its refresh
    /// timer, warehouse upgrades, and the accept transaction. Orchestrates the Domain pieces and
    /// calls into <see cref="KingdomLedger"/> to actually move resources.
    /// </summary>
    /// <remarks>
    /// <para><b>The constructor does no work.</b> Offers come from <see cref="InitializeNewGame"/>
    /// or <see cref="LoadFromDto"/>, never from construction. The previous version generated a
    /// batch in its constructor — before the save file had even been read — which meant every
    /// launch produced a fresh set of offers and relaunching the app was a free instant refresh,
    /// bypassing the crystal price charged for exactly that.</para>
    ///
    /// <para><b>No <c>EventBus</c> dependency.</b> The only subscriber to this module's state is
    /// its own Presenter, which already holds this object, so a plain <c>event Action</c> is
    /// simpler and typed (<c>ARCHITECTURE.md</c> §4.2 tier 3) — the same call
    /// <c>LawsManager.QueueChanged</c> makes. The Ledger still publishes its own cross-module
    /// <c>TradeResourceChangedEvent</c> whenever this class moves resources through it.</para>
    ///
    /// <para><b>Warehouse capacity is derived, never stored here.</b> The upgrade <i>level</i> is
    /// this module's state; capacity is computed from it through the curve and handed to the
    /// Ledger, which derives the regen rate. That keeps one source of truth for a number that
    /// used to live in three places.</para>
    /// </remarks>
    public sealed class TradeManager
    {
        private readonly KingdomLedger _ledger;
        private readonly IClock        _clock;
        private readonly TradeConfig   _config;

        private readonly TradeOfferGenerator _offerGenerator;
        private readonly OfferRefreshTimer   _refreshTimer = new();

        private readonly List<TradeOffer> _activeOffers = new();
        private readonly Dictionary<TradeResourceType, int> _warehouseLevels = new();

        /// <summary>
        /// Raised after the offer list or a warehouse level changes. A plain event, not a bus
        /// message: the only subscriber is this module's Presenter (<c>ARCHITECTURE.md</c> §4.2).
        /// Promote it to a Ledger-tier event only if something outside Trade needs it — a
        /// bottom-nav badge counting profitable offers would be the trigger.
        /// </summary>
        public event Action TradeStateChanged;

        public IReadOnlyList<TradeOffer> ActiveOffers => _activeOffers;

        /// <summary>When the offer list is next replaced. Null before the first initialise.</summary>
        public DateTime? NextRefreshDueUtc => _refreshTimer.NextDueUtc;

        public TradeManager(KingdomLedger ledger, IClock clock, TradeConfig config)
            : this(ledger, clock, config, rng: null) { }

        /// <summary>
        /// Deterministic overload for tests, so a batch of offers can be reproduced exactly.
        /// </summary>
        /// <remarks>
        /// VContainer picks the constructor with the most parameters, so left alone it would
        /// select this one and fail to resolve <c>System.Random</c> at startup. The explicit
        /// factory in <c>GameBootstrapper</c> pins the 3-argument one — the same trap
        /// <c>LawsManager</c> documents in <c>docs/modules/Laws.md</c> §6.8.
        /// </remarks>
        public TradeManager(KingdomLedger ledger, IClock clock, TradeConfig config, System.Random rng)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _clock  = clock  ?? throw new ArgumentNullException(nameof(clock));
            _config = config ?? throw new ArgumentNullException(nameof(config));

            _offerGenerator = rng != null
                ? new TradeOfferGenerator(rng)
                : new TradeOfferGenerator();

            foreach (TradeResourceType type in Enum.GetValues(typeof(TradeResourceType)))
                _warehouseLevels[type] = 0;

            // No unsubscribe: the manager and the clock are root-scope singletons and live
            // exactly as long as each other.
            _clock.Ticked += AdvanceTo;
        }

        // ── Warehouse state ───────────────────────────────────────────────────────

        public int GetWarehouseLevel(TradeResourceType type) => _warehouseLevels[type];

        /// <summary>The resource whose warehouse pays for this one's upgrade (GDD §7).</summary>
        public TradeResourceType GetPairedResource(TradeResourceType type) =>
            TradeResourcePair.GetPairedResource(type);

        // ── Prices ────────────────────────────────────────────────────────────────
        // On the Manager, beside the transactions that charge them. A price the UI computes
        // separately from the price actually deducted is a monetization bug waiting to happen —
        // see docs/modules/Laws.md §6.3.

        public int InstantRefreshCost => _config.InstantRefreshCrystalCost;

        public int GetWarehouseCrystalCost(TradeResourceType type) =>
            _config.WarehouseUpgradeCrystalCost(_warehouseLevels[type]);

        /// <summary>Fraction of the paired warehouse's capacity the resource path costs (GDD §7).</summary>
        public const float PairedUpgradeCostFraction = 0.8f;

        /// <summary>
        /// The paired-resource upgrade price: 80% of the paired warehouse's <b>capacity</b>, paid
        /// out of the paired resource's <b>stored amount</b>.
        /// </summary>
        /// <remarks>
        /// Capacity, deliberately, not the stored amount — the reading GDD §7 flagged for
        /// confirmation, and it was confirmed. The player must be at least 80% full of the paired
        /// resource to afford it, so the two warehouses in a pair advance in step. Pricing it off
        /// the stored amount instead would make it cheapest exactly when the player has least.
        /// </remarks>
        public float GetWarehousePairedCost(TradeResourceType type) =>
            _ledger.GetTradeResource(GetPairedResource(type)).Capacity * PairedUpgradeCostFraction;

        /// <summary>Whether this warehouse can still be upgraded (see <see cref="TradeConfig.MaxWarehouseLevel"/>).</summary>
        public bool CanUpgradeFurther(TradeResourceType type) =>
            _warehouseLevels[type] < TradeConfig.MaxWarehouseLevel;

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        /// <summary>Seed a brand-new game: base warehouses and a first batch of offers.</summary>
        public void InitializeNewGame()
        {
            foreach (TradeResourceType type in Enum.GetValues(typeof(TradeResourceType)))
            {
                _warehouseLevels[type] = 0;
                ApplyCapacityFor(type);
            }

            // Start the regen clock from the injected clock. Without this a new game measures its
            // first accrual from whenever the Ledger happened to be constructed.
            _ledger.ResetRegenBaseline(_clock.UtcNow);

            RefreshOffers();
            _refreshTimer.Start(_clock.UtcNow, _config.OfferRefreshTimeSeconds);
            TradeStateChanged?.Invoke();
        }

        /// <summary>
        /// Settle the offer refresh up to <paramref name="now"/>. Called on every clock tick, and
        /// cheap when nothing is due. Timestamp-based, so one call settles an absence of any
        /// length (<c>ARCHITECTURE.md</c> §4.5).
        /// </summary>
        /// <remarks>
        /// Warehouse regeneration is <b>not</b> settled here: it is the Ledger's, and advances on
        /// the clock by itself. The refresh does not read amounts, so it does not need it — what
        /// does read amounts settles regen itself first (<see cref="AcceptOffer"/>, the upgrades).
        /// </remarks>
        public void AdvanceTo(DateTime now)
        {
            if (!_refreshTimer.Advance(now, _config.OfferRefreshTimeSeconds)) return;

            RefreshOffers();
            TradeStateChanged?.Invoke();
        }

        /// <summary>Seconds until the offer list is replaced.</summary>
        public float GetSecondsUntilRefresh() => _refreshTimer.SecondsUntilNext(_clock.UtcNow);

        // ── Offers ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Work out whether an offer can be taken and what to warn about, <b>without changing
        /// anything</b>. Drives the confirmation panel.
        /// </summary>
        public TradeOfferEvaluation Evaluate(string offerId)
        {
            var offer = FindOffer(offerId);
            if (offer == null) return TradeOfferEvaluation.Missing();

            var issues = new List<TradeIssue>();

            foreach (var pair in offer.GiveResources)
            {
                float held = _ledger.GetTradeResource(pair.Key).Amount;
                if (held < pair.Value)
                    issues.Add(new TradeIssue(
                        TradeIssueKind.InsufficientResource, pair.Key, pair.Value, held));
            }

            foreach (var pair in offer.ReceiveResources)
            {
                var  state = _ledger.GetTradeResource(pair.Key);
                float room = Math.Max(0f, state.Capacity - state.Amount);
                if (room < pair.Value)
                    issues.Add(new TradeIssue(
                        TradeIssueKind.WouldOverflowWarehouse, pair.Key, pair.Value, room));
            }

            return new TradeOfferEvaluation(true, issues);
        }

        /// <summary>
        /// Take an offer: spend the give side, credit the receive side, remove it from the list.
        /// </summary>
        /// <remarks>
        /// <para><b>A full warehouse does not stop this.</b> Overflow is a warning the player has
        /// already seen on the confirmation panel; they may proceed and forfeit the excess. Only
        /// being unable to pay the give side refuses the trade — and a refusal spends nothing.</para>
        ///
        /// <para>Regen is accrued first so a player who crossed the affordability threshold a
        /// fraction of a second ago is honoured. It deliberately does <b>not</b> call
        /// <see cref="AdvanceTo"/>, which could refresh the offer list out from under the trade
        /// being accepted.</para>
        /// </remarks>
        public TradeAcceptResult AcceptOffer(string offerId)
        {
            _ledger.AccruePassiveResourceRegen(_clock.UtcNow);

            var offer = FindOffer(offerId);
            if (offer == null)
                return TradeAcceptResult.Refused(new[] { TradeIssue.Unavailable() });

            var evaluation = Evaluate(offerId);
            if (!evaluation.CanAccept)
                return TradeAcceptResult.Refused(evaluation.Issues);

            foreach (var pair in offer.GiveResources)
            {
                if (_ledger.SpendTradeResource(pair.Key, pair.Value)) continue;

                // Evaluate just said this was affordable, so reaching here means the ledger and
                // the evaluation disagree — a real invariant break, not a player-facing case.
                Debug.LogError(
                    $"[TradeManager] Offer '{offerId}' passed evaluation but spending " +
                    $"{pair.Value} {pair.Key} failed. The trade is now partially applied.");
            }

            var received  = new Dictionary<TradeResourceType, float>();
            var forfeited = new Dictionary<TradeResourceType, float>();

            foreach (var pair in offer.ReceiveResources)
            {
                // The return value is how much actually fit. Discarding it is what used to
                // destroy resources silently when a warehouse was full.
                float actual = _ledger.AddTradeResource(pair.Key, pair.Value);
                received[pair.Key] = actual;

                float lost = pair.Value - actual;
                if (lost > 0f) forfeited[pair.Key] = lost;
            }

            _activeOffers.Remove(offer);
            TradeStateChanged?.Invoke();

            return TradeAcceptResult.Success(received, forfeited, evaluation.Issues);
        }

        /// <summary>Replace the offer list immediately for crystals, resetting the timer.</summary>
        public bool InstantRefresh()
        {
            if (!_ledger.SpendCrystals(InstantRefreshCost)) return false;

            RefreshOffers();
            _refreshTimer.Start(_clock.UtcNow, _config.OfferRefreshTimeSeconds);
            TradeStateChanged?.Invoke();
            return true;
        }

        // ── Warehouse upgrades ────────────────────────────────────────────────────

        /// <summary>Upgrade a warehouse by paying crystals (GDD §7's premium path).</summary>
        /// <remarks>
        /// Regen is settled first: the capacity is about to change, and regen accrued since the
        /// last tick must be counted against the capacity it was earned under.
        /// </remarks>
        public bool UpgradeWarehouseWithCrystals(TradeResourceType type)
        {
            _ledger.AccruePassiveResourceRegen(_clock.UtcNow);
            if (!CanUpgradeFurther(type)) return false;
            if (!_ledger.SpendCrystals(GetWarehouseCrystalCost(type))) return false;

            RaiseLevel(type);
            return true;
        }

        /// <summary>
        /// Upgrade a warehouse by spending 80% of the paired warehouse's <b>capacity</b>, taken
        /// from that resource's stored amount (GDD §7).
        /// </summary>
        /// <remarks>
        /// Regen is settled first, for two reasons: the paired resource's stored amount decides
        /// whether this is affordable, and this warehouse's capacity is about to change.
        /// </remarks>
        public bool UpgradeWarehouseWithPairedResource(TradeResourceType type)
        {
            _ledger.AccruePassiveResourceRegen(_clock.UtcNow);
            if (!CanUpgradeFurther(type)) return false;

            var  paired = GetPairedResource(type);
            float cost  = GetWarehousePairedCost(type);

            if (!_ledger.SpendTradeResource(paired, cost)) return false;

            RaiseLevel(type);
            return true;
        }

        // ── Persistence ───────────────────────────────────────────────────────────

        public TradeStateDto ToDto()
        {
            var dto = new TradeStateDto
            {
                NextOfferRefreshDueUtc = _refreshTimer.NextDueUtc.HasValue
                    ? _refreshTimer.NextDueUtc.Value.ToString("O")
                    : null,
                WarehouseLevels = new Dictionary<string, int>(),
                ActiveOffers    = new List<TradeOfferDto>(_activeOffers.Count)
            };

            foreach (var pair in _warehouseLevels)
                dto.WarehouseLevels[pair.Key.ToString()] = pair.Value;

            foreach (var offer in _activeOffers)
                dto.ActiveOffers.Add(ToOfferDto(offer));

            return dto;
        }

        /// <summary>
        /// Restore from a save. A null DTO means a fresh game.
        /// </summary>
        /// <remarks>
        /// Order matters: warehouse levels are restored and capacities re-asserted <b>before</b>
        /// any accrual runs, so regen is never computed against a capacity that is about to
        /// change. That ordering bug is why the standalone accrual call was removed from
        /// <c>GameStateCoordinator</c> — the two sites fought each other.
        /// </remarks>
        public void LoadFromDto(TradeStateDto dto)
        {
            if (dto == null) { InitializeNewGame(); return; }

            RestoreWarehouseLevels(dto.WarehouseLevels);
            RestoreOffers(dto.ActiveOffers);

            _refreshTimer.Restore(
                ParseUtcOrNull(dto.NextOfferRefreshDueUtc),
                _clock.UtcNow,
                _config.OfferRefreshTimeSeconds);

            // A save from before offers were persisted has none; give the player a batch rather
            // than an empty screen.
            if (_activeOffers.Count == 0) RefreshOffers();

            // Offline catch-up. Regen first, and here rather than on the next clock tick: the
            // capacities were only just restored above, and this is the first moment they are
            // final (see the remarks on this method).
            _ledger.AccruePassiveResourceRegen(_clock.UtcNow);
            AdvanceTo(_clock.UtcNow);
            TradeStateChanged?.Invoke();
        }

        // ── Private ───────────────────────────────────────────────────────────────

        private void RestoreWarehouseLevels(Dictionary<string, int> levels)
        {
            foreach (TradeResourceType type in Enum.GetValues(typeof(TradeResourceType)))
            {
                int level = 0;
                if (levels != null &&
                    levels.TryGetValue(type.ToString(), out int saved))
                {
                    // Clamped, not trusted: the save is plain JSON on the device, and a
                    // hand-edited level would otherwise produce an infinite capacity, an
                    // infinite regen rate and NaN amounts — an unrecoverable economy.
                    level = Math.Clamp(saved, 0, TradeConfig.MaxWarehouseLevel);
                }

                _warehouseLevels[type] = level;
                ApplyCapacityFor(type);
            }
        }

        private void RestoreOffers(List<TradeOfferDto> offers)
        {
            _activeOffers.Clear();
            if (offers == null) return;

            foreach (var dto in offers)
            {
                var offer = FromOfferDto(dto);
                if (offer != null) _activeOffers.Add(offer);
            }
        }

        /// <summary>
        /// Push this resource's capacity into the Ledger, which derives the regen rate from it.
        /// The level is the stored fact; capacity is always computed from it.
        /// </summary>
        private void ApplyCapacityFor(TradeResourceType type)
        {
            float capacity = _config.WarehouseCapacityAt(_warehouseLevels[type]);
            _ledger.SetWarehouseCapacity(type, capacity);
        }

        private void RaiseLevel(TradeResourceType type)
        {
            _warehouseLevels[type]++;
            ApplyCapacityFor(type);
            TradeStateChanged?.Invoke();
        }

        private void RefreshOffers()
        {
            _activeOffers.Clear();
            _activeOffers.AddRange(
                _offerGenerator.GenerateBatch(_config.OfferCount, _config.OfferBaseAmount));
        }

        private TradeOffer FindOffer(string offerId)
        {
            if (string.IsNullOrEmpty(offerId)) return null;
            return _activeOffers.Find(offer => offer.Id == offerId);
        }

        private static TradeOfferDto ToOfferDto(TradeOffer offer)
        {
            var dto = new TradeOfferDto
            {
                Id            = offer.Id,
                Profitability = offer.Profitability.ToString()
            };

            foreach (var pair in offer.GiveResources)    dto.Give[pair.Key.ToString()]    = pair.Value;
            foreach (var pair in offer.ReceiveResources) dto.Receive[pair.Key.ToString()] = pair.Value;

            return dto;
        }

        /// <summary>Rebuild an offer from a save, or null if it is too damaged to trust.</summary>
        private static TradeOffer FromOfferDto(TradeOfferDto dto)
        {
            if (dto == null || string.IsNullOrEmpty(dto.Id)) return null;

            var give    = ParseAmounts(dto.Give);
            var receive = ParseAmounts(dto.Receive);

            // An offer with nothing on a side would be a free resource or a no-op trade.
            if (give.Count == 0 || receive.Count == 0) return null;

            if (!Enum.TryParse<TradeProfitability>(dto.Profitability, out var profitability))
                profitability = TradeProfitability.Neutral;

            return new TradeOffer(dto.Id, give, receive, profitability);
        }

        private static Dictionary<TradeResourceType, float> ParseAmounts(Dictionary<string, float> source)
        {
            var result = new Dictionary<TradeResourceType, float>();
            if (source == null) return result;

            foreach (var pair in source)
            {
                // Unknown name: a resource that existed in an older build. Skip it rather than
                // failing the whole load.
                if (!Enum.TryParse<TradeResourceType>(pair.Key, out var type)) continue;
                if (pair.Value <= 0f) continue;
                result[type] = pair.Value;
            }

            return result;
        }

        private static DateTime? ParseUtcOrNull(string iso) =>
            DateTime.TryParse(iso, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : (DateTime?)null;
    }
}
