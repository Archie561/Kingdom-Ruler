using System;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Systems.Clock;
using KingdomRuler.Systems.Save;
using KingdomRuler.Modules.Laws.Domain;
using UnityEngine;

namespace KingdomRuler.Modules.Laws
{
    /// <summary>
    /// Model layer for the Laws mechanic: owns the card queue and coordinates the
    /// domain pieces that make it work.
    /// </summary>
    /// <remarks>
    /// <para>The mechanics themselves live in <c>Domain/</c> and are pure C#:
    /// <see cref="ShuffleBagDeck"/> decides draw order, <see cref="ReplenishmentSlots"/>
    /// owns the timers, and <see cref="LawCardEffectApplier"/> translates a card's effects
    /// into Ledger calls. This class holds only the state that spans them — which card is
    /// active — plus the Ledger and config wiring.</para>
    ///
    /// <para><b>Queue model (single active card).</b> The player "holds" up to
    /// <see cref="MaxCardCount"/> cards, but only one is ever shown — so the rest are a
    /// count, not identities. Cards are drawn at the moment they are shown, which is what
    /// keeps the save down to one active id plus the remaining shuffle-bag cycle.</para>
    /// <code>
    ///   AvailableCardCount  +  CardsReplenishing  ==  MaxCardCount
    /// </code>
    /// <para>Those two are complements and between them describe the queue completely:
    /// one counts what the player has (including the active card), the other counts the
    /// timers still running. Only the second is stored — as
    /// <see cref="ReplenishmentSlots.Count"/> — so the invariant cannot drift.</para>
    ///
    /// <para>If a future design really does show several cards at once,
    /// <see cref="ActiveCard"/> is the field that becomes a collection again.</para>
    /// </remarks>
    public sealed class LawsManager
    {
        private readonly KingdomLedger      _ledger;
        private readonly IClock             _clock;
        private readonly LawsConfig         _config;
        private readonly ShuffleBagDeck     _deck;
        private readonly ReplenishmentSlots _slots;

        /// <summary>
        /// Raised when the card queue changes structurally: a card arrived, a card was
        /// resolved, a slot finished replenishing, or an instant refill completed.
        ///
        /// A plain event rather than an event-bus message, per ARCHITECTURE.md §4.2:
        /// the only subscriber is this module's own Presenter, which already holds a
        /// direct reference here. If something outside Laws ever needs to observe the
        /// queue (a bottom-nav badge, say), that's the signal to promote it to a
        /// cross-module bus event instead.
        /// </summary>
        public event Action QueueChanged;

        /// <summary>The one card currently presented to the player; null when none is.</summary>
        private LawCardDefinition _activeCard;

        // ── Public read-only surface ──────────────────────────────────────────────

        /// <summary>The card the player is looking at, or null.</summary>
        public LawCardDefinition ActiveCard        => _activeCard;
        public bool              HasActiveCard     => _activeCard != null;
        public int               CardsReplenishing => _slots.Count;

        /// <summary>The cap from config — the "8" in a "6/8" readout.</summary>
        public int MaxCardCount => _config.MaxHeldCards;

        /// <summary>
        /// Cards the player holds right now, the active one included — the number the
        /// UI shows as "N/8" and the one the player thinks in.
        /// </summary>
        public int AvailableCardCount => Math.Max(0, _config.MaxHeldCards - _slots.Count);

        // ── Constructor ───────────────────────────────────────────────────────────

        public LawsManager(KingdomLedger ledger, IClock clock, LawsConfig config)
            : this(ledger, clock, config, rng: null) { }

        /// <summary>Overload taking an explicit RNG so draw order is reproducible in tests.</summary>
        public LawsManager(KingdomLedger ledger, IClock clock, LawsConfig config, System.Random rng)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _clock  = clock  ?? throw new ArgumentNullException(nameof(clock));
            _config = config ?? throw new ArgumentNullException(nameof(config));

            _deck  = new ShuffleBagDeck(rng);
            _slots = new ReplenishmentSlots();
        }

        // ── Initialization ────────────────────────────────────────────────────────

        /// <summary>
        /// Start a fresh game: shuffle the full card set and begin replenishing every
        /// slot. For resuming a saved game use <see cref="LoadFromDto"/> instead.
        /// </summary>
        public void InitializeCardPool()
        {
            _deck.SetContents(_config.AllCards);
            _slots.StartAll(_config.MaxHeldCards, _clock.UtcNow, _config.CardReplenishTimeSeconds);
        }

        // ── Replenishment ─────────────────────────────────────────────────────────

        /// <summary>
        /// Settle the replenishment timers against the clock. Driven by
        /// <c>AccrualDriver</c>; cheap to call repeatedly.
        /// </summary>
        public void ProcessReplenishment()
        {
            int matured = _slots.Advance(_clock.UtcNow, _config.CardReplenishTimeSeconds);
            if (matured <= 0) return;

            // Only one card is ever displayed, so however many timers matured, at most one
            // can become the active card here. The rest simply raise AvailableCardCount.
            TryFillActiveSlot();

            // Every matured slot is player-visible even without a draw: the held-card
            // readout rises and the countdown may stop entirely.
            QueueChanged?.Invoke();
        }

        // ── Card resolution ───────────────────────────────────────────────────────

        /// <summary>
        /// Accept or reject the active card, identified by id to avoid acting on a
        /// different card than the player saw. Effects hit the Ledger immediately.
        /// </summary>
        public bool ResolveCard(string cardId, bool accept)
        {
            if (_activeCard == null || _activeCard.CardId != cardId)
                return false;

            var card = _activeCard;
            LawCardEffectApplier.Apply(_ledger, accept ? card.AcceptEffects : card.RejectEffects);

            _activeCard = null;

            // The slot this card occupied is now free, so put a timer on it — capped so
            // the queue can never hold more than MaxHeldCards.
            if (_slots.Count < _config.MaxHeldCards)
                _slots.AddOne(_clock.UtcNow, _config.CardReplenishTimeSeconds);

            TryFillActiveSlot();

            QueueChanged?.Invoke();
            return true;
        }

        // ── Crystal spends ────────────────────────────────────────────────────────

        // Prices live here, next to the transactions that charge them. A Presenter that
        // derived its own display price from the same config would be a second
        // implementation of the same formula — and a price shown that drifts from the
        // price charged is a monetization bug, not a cosmetic one.

        /// <summary>
        /// Crystals to instantly mature every pending timer
        /// (GDD §6: <c>CrystalCostPerRefill</c> per missing card). 0 when nothing is running.
        /// </summary>
        public int RefillCost => _slots.Count * _config.CrystalCostPerRefill;

        /// <summary>
        /// Crystals to finish this characteristic's current level.
        /// 0 when there is nothing left to buy, so callers can gate the offer on it.
        /// </summary>
        public int GetBuyUpCost(CharacteristicType type) =>
            CrystalBuyUpCalculator.CalculateCost(
                _ledger.PointsRemainingForNextLevel(type), _config.CrystalBuyUpDivisor);

        /// <summary>Instantly mature every pending timer for crystals.</summary>
        public bool RefillWithCrystals()
        {
            if (!_slots.IsRunning) return false;

            if (!_ledger.SpendCrystals(RefillCost)) return false;

            _slots.CancelAll();
            TryFillActiveSlot();

            QueueChanged?.Invoke();
            return true;
        }

        /// <summary>Spend crystals to finish the current level of a characteristic.</summary>
        public bool BuyUpCharacteristic(CharacteristicType type)
        {
            int crystalCost = GetBuyUpCost(type);

            if (crystalCost <= 0)
            {
                // A zero price means nothing is left to buy. The Ledger rolls a
                // characteristic over the moment it has enough points, so reaching here
                // means that invariant is broken — say so rather than charging the player
                // for a purchase that would do nothing.
                Debug.LogError(
                    $"[LawsManager] BuyUpCharacteristic: nothing remaining for {type}. " +
                    "Ledger rollover invariant may be violated — refusing to charge crystals.");
                return false;
            }

            if (!_ledger.SpendCrystals(crystalCost)) return false;

            _ledger.AddCharacteristicPoints(type, _ledger.PointsRemainingForNextLevel(type));
            return true;
        }

        // ── Timer query ───────────────────────────────────────────────────────────

        /// <summary>Seconds until the next card matures; 0 when nothing is replenishing.</summary>
        public float GetSecondsUntilNextCard() =>
            _slots.SecondsUntilNext(_clock.UtcNow);

        /// <summary>
        /// Seconds until every replenishing slot has matured — what the refill prompt offers
        /// to skip. Zero when the queue is already full.
        /// </summary>
        /// <remarks>
        /// The next slot lands after its own remaining time; each one behind it takes a whole
        /// interval after that. Derived rather than stored, for the same reason the queue
        /// stores one deadline and not N timers (§6.2).
        /// </remarks>
        public float GetSecondsUntilAllCards()
        {
            if (_slots.Count == 0) return 0f;

            return _slots.SecondsUntilNext(_clock.UtcNow)
                 + (_slots.Count - 1) * _config.CardReplenishTimeSeconds;
        }

        // ── Save / Load ───────────────────────────────────────────────────────────

        /// <summary>
        /// Restore from save data, then immediately settle whatever elapsed while the
        /// game was closed (ARCHITECTURE.md §4.5).
        /// </summary>
        public void LoadFromDto(LawsStateDto dto)
        {
            if (dto == null)
            {
                InitializeCardPool();
                return;
            }

            _deck.SetContents(_config.AllCards);
            _deck.RestoreCycle(dto.RemainingDeckCardIds, dto.LastDrawnCardId);

            _activeCard = _deck.FindById(dto.ActiveCardId);

            // If the deadline is missing or unparsable (an older save, or a damaged file),
            // fall back to one fresh interval from now rather than dropping the queue.
            _slots.Restore(
                dto.CardsReplenishing,
                ParseUtcOr(dto.NextReplenishDueUtc,
                           _clock.UtcNow.AddSeconds(_config.CardReplenishTimeSeconds)));

            ProcessReplenishment();
        }

        /// <summary>Capture current state for saving.</summary>
        public LawsStateDto ToDto() => new LawsStateDto
        {
            ActiveCardId          = _activeCard != null ? _activeCard.CardId : null,
            LastDrawnCardId       = _deck.LastDrawnCardId,
            RemainingDeckCardIds  = _deck.RemainingCardIds(),
            CardsReplenishing     = _slots.Count,
            NextReplenishDueUtc   = _slots.NextDueUtc.HasValue
                                        ? _slots.NextDueUtc.Value.ToString("O")
                                        : null
        };

        // ── Private ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Draw into the active slot if it is empty and a matured card is waiting.
        /// </summary>
        /// <remarks>
        /// Past the first guard there is no active card, so every available card is by
        /// definition one that has matured and is waiting — which is why this needs only
        /// <see cref="AvailableCardCount"/> and not a separate "ready" count.
        /// </remarks>
        private void TryFillActiveSlot()
        {
            if (HasActiveCard) return;
            if (AvailableCardCount <= 0) return;

            _activeCard = _deck.Draw();
        }

        private static DateTime ParseUtcOr(string iso, DateTime fallback) =>
            DateTime.TryParse(iso, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : fallback;
    }
}
