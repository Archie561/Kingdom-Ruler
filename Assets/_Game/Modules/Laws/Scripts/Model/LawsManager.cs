using System;
using System.Globalization;
using UnityEngine;
using VContainer;
using KingdomRuler.Systems.Clock;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Systems.Save;

namespace KingdomRuler.Modules.Laws
{
    /// <summary>
    /// The Laws mechanic's entry point: its lifecycle, its clock, and every Ledger transaction it
    /// makes. The only class in the module that touches the Ledger or the clock.
    /// </summary>
    public sealed class LawsManager
    {
        private readonly KingdomLedger _ledger;
        private readonly IClock        _clock;
        private readonly LawsConfig    _config;
        private readonly LawQueue      _queue;

        /// <summary>Raised whenever the queue changes: a card resolved, matured or was refilled.</summary>
        public event Action QueueChanged;

        [Inject]
        public LawsManager(KingdomLedger ledger, IClock clock, LawsConfig config)
            : this(ledger, clock, config, new System.Random()) { }

        /// <summary>Takes a seeded RNG so tests can pin the draw order.</summary>
        public LawsManager(KingdomLedger ledger, IClock clock, LawsConfig config, System.Random rng)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _clock  = clock  ?? throw new ArgumentNullException(nameof(clock));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _queue  = new LawQueue(config, rng);

            // The manager and the clock are root-scope singletons, so this is never unsubscribed.
            _clock.Ticked += AdvanceTo;
        }

        // ── Queue ─────────────────────────────────────────────────────────────────

        public LawCardDefinition ActiveCard   => _queue.ActiveCard;
        public int HeldCardCount              => _queue.HeldCount;
        public int ReplenishingCardCount      => _queue.ReplenishingCount;
        public int MaxCardCount               => _queue.MaxCount;
        public float SecondsUntilNextCard     => _queue.SecondsUntilNextCard(_clock.UtcNow);
        public float SecondsUntilQueueFull    => _queue.SecondsUntilFull(_clock.UtcNow);

        /// <summary>Settle replenishment up to <paramref name="now"/>. Runs on every clock tick.</summary>
        public void AdvanceTo(DateTime now)
        {
            if (_queue.Advance(now)) QueueChanged?.Invoke();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        public void InitializeNewGame()
        {
            WarnIfNoCards();
            _queue.StartEmpty(_clock.UtcNow);
        }

        /// <summary>Restore a save, then settle the time the app was closed.</summary>
        public void LoadFromDto(LawsStateDto dto)
        {
            if (dto == null)
            {
                InitializeNewGame();
                return;
            }

            WarnIfNoCards();
            var now = _clock.UtcNow;

            // A missing or unreadable deadline restarts the countdown rather than dropping the queue.
            var nextDue = DateTime.TryParse(dto.NextReplenishDueUtc, null,
                                            DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : now.AddSeconds(_config.CardReplenishTimeSeconds);

            _queue.Restore(dto.ActiveCardId, dto.RemainingDeckCardIds, dto.LastDrawnCardId,
                           dto.CardsReplenishing, nextDue);
            AdvanceTo(now);
        }

        public LawsStateDto ToDto() => new LawsStateDto
        {
            ActiveCardId         = _queue.ActiveCard != null ? _queue.ActiveCard.CardId : null,
            LastDrawnCardId      = _queue.LastDrawnCardId,
            RemainingDeckCardIds = _queue.RemainingDeckCardIds(),
            CardsReplenishing    = _queue.ReplenishingCount,
            NextReplenishDueUtc  = _queue.NextReplenishDueUtc?.ToString("O")
        };

        // ── Card decisions ────────────────────────────────────────────────────────

        /// <summary>Apply the shown card's effects for this decision and move the queue on.</summary>
        public bool ResolveActiveCard(LawDecision decision)
        {
            var card = _queue.TakeActiveCard(_clock.UtcNow);
            if (card == null) return false;

            ApplyEffects(card.EffectsOf(decision));
            QueueChanged?.Invoke();
            return true;
        }

        // ── Crystal purchases ─────────────────────────────────────────────────────
        // Each price sits next to the transaction that charges it, so the price shown is the price taken.

        /// <summary>Crystals to mature every replenishing card now. 0 when nothing is replenishing.</summary>
        public int RefillCost => _queue.ReplenishingCount * _config.CrystalCostPerRefill;

        public bool CanAffordRefill => RefillCost > 0 && _ledger.Crystals >= RefillCost;

        public bool TryRefill()
        {
            if (RefillCost <= 0 || !_ledger.SpendCrystals(RefillCost)) return false;

            _queue.CompleteReplenishment();
            QueueChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Crystals to finish this characteristic's current level: <c>ceil(remaining / divisor)</c>,
        /// minimum 1. 0 when nothing remains.
        /// </summary>
        public int BuyUpCost(CharacteristicType type)
        {
            if (_config.CrystalBuyUpDivisor <= 0f)
                throw new InvalidOperationException("LawsConfig.CrystalBuyUpDivisor must be greater than 0.");

            float remaining = _ledger.PointsRemainingForNextLevel(type);
            return remaining <= 0f ? 0 : Math.Max(1, (int)Math.Ceiling(remaining / _config.CrystalBuyUpDivisor));
        }

        public bool CanAffordBuyUp(CharacteristicType type)
        {
            int cost = BuyUpCost(type);
            return cost > 0 && _ledger.Crystals >= cost;
        }

        public bool TryBuyUp(CharacteristicType type)
        {
            int cost = BuyUpCost(type);
            if (cost <= 0)
            {
                // The Ledger levels a characteristic up the moment it has enough points, so there is
                // always something left to buy. Refuse rather than charge for nothing.
                Debug.LogError($"[LawsManager] Nothing left to buy for {type} — refusing to charge crystals.");
                return false;
            }

            if (!_ledger.SpendCrystals(cost)) return false;

            _ledger.AddCharacteristicPoints(type, _ledger.PointsRemainingForNextLevel(type));
            return true;
        }

        // ── Private ───────────────────────────────────────────────────────────────

        private void ApplyEffects(LawCardEffect[] effects)
        {
            if (effects == null) return;

            // Cards author one signed number; the Ledger takes gains and losses through separate methods.
            foreach (var effect in effects)
            {
                if (effect.Points >= 0f)
                    _ledger.AddCharacteristicPoints(effect.Characteristic, effect.Points);
                else
                    _ledger.ReduceCharacteristicPoints(effect.Characteristic, -effect.Points);
            }
        }

        private void WarnIfNoCards()
        {
            if (_config.AllCards == null || _config.AllCards.Length == 0)
                Debug.LogWarning("[LawsManager] LawsConfig has no cards — the Laws screen will stay in its waiting state.");
        }
    }
}
