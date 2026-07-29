using System;
using System.Collections.Generic;
using KingdomRuler.Core;
using KingdomRuler.Shared.Ledger;
using KingdomRuler.Shared.Services;
using KingdomRuler.Modules.Laws.Domain;

namespace KingdomRuler.Modules.Laws
{
    /// <summary>
    /// Model layer for the Laws mechanic. Manages card queue, replenishment
    /// timers, card resolution, crystal refill, and crystal buy-up.
    /// </summary>
    public sealed class LawsManager
    {
        private readonly KingdomLedger _ledger;
        private readonly EventBus _eventBus;
        private readonly IClock _clock;
        private readonly LawsConfig _config;

        private readonly List<LawCardDefinition> _heldCards = new();
        private readonly Queue<LawCardDefinition> _cardPool = new();
        private DateTime _lastReplenishCheckUtc;
        private int _cardsReplenishing;

        public IReadOnlyList<LawCardDefinition> HeldCards => _heldCards;
        public int CardsReplenishing => _cardsReplenishing;
        public DateTime LastReplenishCheckUtc => _lastReplenishCheckUtc;

        public LawsManager(
            KingdomLedger ledger,
            EventBus eventBus,
            IClock clock,
            LawsConfig config)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _lastReplenishCheckUtc = _clock.UtcNow;
            _cardsReplenishing = 0;
        }

        /// <summary>
        /// Initialize the card pool from available card definitions.
        /// Called during game setup after content is loaded.
        /// Starts replenishment for any empty card slots.
        /// </summary>
        public void InitializeCardPool(IEnumerable<LawCardDefinition> allCards)
        {
            _cardPool.Clear();
            foreach (var card in allCards)
                _cardPool.Enqueue(card);

            // Begin replenishing any empty card slots
            int emptySlots = _config.MaxHeldCards - _heldCards.Count;
            if (emptySlots > 0 && _cardsReplenishing == 0)
            {
                _cardsReplenishing = emptySlots;
                _lastReplenishCheckUtc = _clock.UtcNow;
            }
        }

        /// <summary>
        /// Process offline/idle accrual for card replenishment.
        /// Called on app resume or save load.
        /// </summary>
        public void ProcessReplenishment()
        {
            if (_cardsReplenishing <= 0) return;

            var now = _clock.UtcNow;
            var elapsedSeconds = (float)(now - _lastReplenishCheckUtc).TotalSeconds;
            if (elapsedSeconds <= 0) return;

            var secondsPerCard = _config.CardReplenishTimeSeconds;
            if (secondsPerCard <= 0) return;

            while (elapsedSeconds >= secondsPerCard && _cardsReplenishing > 0)
            {
                elapsedSeconds -= secondsPerCard;
                _cardsReplenishing--;

                if (_heldCards.Count < _config.MaxHeldCards && _cardPool.Count > 0)
                {
                    var card = _cardPool.Dequeue();
                    _heldCards.Add(card);
                    // Recycle to back of pool for infinite replay
                    _cardPool.Enqueue(card);
                }
            }

            _lastReplenishCheckUtc = now - TimeSpan.FromSeconds(elapsedSeconds);
        }

        /// <summary>
        /// Resolve a held card by accepting or rejecting it.
        /// Applies effects to characteristics via the Ledger.
        /// </summary>
        public bool ResolveCard(int cardIndex, bool accept)
        {
            if (cardIndex < 0 || cardIndex >= _heldCards.Count)
                return false;

            var card = _heldCards[cardIndex];
            var effects = accept ? card.AcceptEffects : card.RejectEffects;

            if (effects != null)
            {
                foreach (var effect in effects)
                {
                    Func<int, float> pointsRequired = level =>
                        LevelingMath.PointsRequiredFromCurve(level, _config.LevelingCurve);

                    if (effect.Points >= 0)
                    {
                        _ledger.AddCharacteristicPoints(
                            effect.Characteristic, effect.Points, pointsRequired);
                    }
                    else
                    {
                        _ledger.ReduceCharacteristicPoints(
                            effect.Characteristic, -effect.Points, pointsRequired);
                    }
                }
            }

            _heldCards.RemoveAt(cardIndex);

            // Start replenishing if below cap
            if (_heldCards.Count + _cardsReplenishing < _config.MaxHeldCards)
            {
                _cardsReplenishing++;
                if (_cardsReplenishing == 1)
                    _lastReplenishCheckUtc = _clock.UtcNow;
            }

            return true;
        }

        /// <summary>
        /// Instantly refill held cards to the cap by spending crystals.
        /// Cost: CrystalCostPerRefill per missing card.
        /// Replaces the replenishment queue — you pay to skip the wait.
        /// </summary>
        public bool RefillWithCrystals()
        {
            int totalMissing = _config.MaxHeldCards - _heldCards.Count;
            if (totalMissing <= 0) return false;

            int cost = totalMissing * _config.CrystalCostPerRefill;
            if (!_ledger.SpendCrystals(cost)) return false;

            // Cancel pending replenishment and immediately add cards
            _cardsReplenishing = 0;
            while (_heldCards.Count < _config.MaxHeldCards && _cardPool.Count > 0)
            {
                var card = _cardPool.Dequeue();
                _heldCards.Add(card);
                _cardPool.Enqueue(card);
            }

            return true;
        }

        /// <summary>
        /// Buy up a characteristic to the next level using crystals.
        /// </summary>
        public bool BuyUpCharacteristic(CharacteristicType type)
        {
            var state = _ledger.GetCharacteristic(type);
            Func<int, float> pointsRequired = level =>
                LevelingMath.PointsRequiredFromCurve(level, _config.LevelingCurve);

            float required = pointsRequired(state.Level);
            float remaining = required - state.PointsIntoCurrentLevel;
            if (remaining <= 0) remaining = 0.01f; // edge case: exactly at threshold

            int crystalCost = CrystalBuyUpCalculator.CalculateCost(remaining, _config.CrystalBuyUpDivisor);
            if (!_ledger.SpendCrystals(crystalCost)) return false;

            _ledger.AddCharacteristicPoints(type, remaining, pointsRequired);
            return true;
        }

        /// <summary>
        /// Get the number of seconds until the next card finishes replenishing.
        /// Returns 0 if no cards are replenishing.
        /// </summary>
        public float GetSecondsUntilNextCard()
        {
            if (_cardsReplenishing <= 0) return 0f;
            var elapsed = (float)(_clock.UtcNow - _lastReplenishCheckUtc).TotalSeconds;
            return Math.Max(0f, _config.CardReplenishTimeSeconds - elapsed);
        }

        // --- Save/Load Hydration ---

        /// <summary>Load state from save DTO.</summary>
        public void LoadFromDto(LawsStateDto dto, IEnumerable<LawCardDefinition> allCards)
        {
            // Build lookup
            var cardLookup = new Dictionary<string, LawCardDefinition>();
            foreach (var card in allCards)
            {
                if (card != null && !string.IsNullOrEmpty(card.CardId))
                    cardLookup[card.CardId] = card;
            }

            _heldCards.Clear();
            if (dto.HeldCardIds != null)
            {
                foreach (var id in dto.HeldCardIds)
                {
                    if (cardLookup.TryGetValue(id, out var card))
                        _heldCards.Add(card);
                }
            }

            _cardsReplenishing = dto.CardsReplenishing;
            _lastReplenishCheckUtc = DateTime.TryParse(dto.LastReplenishCheckUtc, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : _clock.UtcNow;

            InitializeCardPool(cardLookup.Values);
        }

        /// <summary>Save current state to DTO.</summary>
        public LawsStateDto ToDto()
        {
            var dto = new LawsStateDto();
            dto.HeldCardIds = new List<string>();
            foreach (var card in _heldCards)
                dto.HeldCardIds.Add(card.CardId);
            dto.CardsReplenishing = _cardsReplenishing;
            dto.LastReplenishCheckUtc = _lastReplenishCheckUtc.ToString("O");
            return dto;
        }
    }
}
