using System;
using System.Collections.Generic;

namespace KingdomRuler.Modules.Laws.Domain
{
    /// <summary>
    /// Draw order for law cards: a shuffle bag (GDD §6).
    /// </summary>
    /// <remarks>
    /// Every card appears exactly once per cycle before any repeats. When the bag empties
    /// it refills from the full set and reshuffles, and the first card of the new cycle is
    /// never the last card of the old one — otherwise the one moment a repeat is most
    /// visible to the player is also the only moment the bag permits it.
    ///
    /// Pure C#: the RNG is injected so draw order is reproducible under test.
    /// </remarks>
    public sealed class ShuffleBagDeck
    {
        private readonly List<LawCardDefinition> _allCards  = new();
        private readonly List<LawCardDefinition> _remaining = new();
        private readonly Random _rng;

        private string _lastDrawnCardId;

        public ShuffleBagDeck(Random rng = null)
        {
            _rng = rng ?? new Random();
        }

        /// <summary>Id of the most recently drawn card, across cycles. Persisted.</summary>
        public string LastDrawnCardId => _lastDrawnCardId;

        /// <summary>Cards left in the current cycle.</summary>
        public int RemainingCount => _remaining.Count;

        /// <summary>Whether any content has been registered at all.</summary>
        public bool IsEmpty => _allCards.Count == 0;

        /// <summary>Ids still to be drawn this cycle, in order. Persisted.</summary>
        public List<string> RemainingCardIds()
        {
            var ids = new List<string>(_remaining.Count);
            foreach (var card in _remaining) ids.Add(card.CardId);
            return ids;
        }

        /// <summary>
        /// Replace the content set and start a fresh, shuffled cycle.
        /// Nulls are dropped so a half-filled config array can't produce null draws.
        /// </summary>
        public void SetContents(IEnumerable<LawCardDefinition> cards)
        {
            _allCards.Clear();
            if (cards != null)
            {
                foreach (var card in cards)
                    if (card != null) _allCards.Add(card);
            }
            ResetCycle();
        }

        /// <summary>Refill from the full set and shuffle.</summary>
        public void ResetCycle()
        {
            _remaining.Clear();
            _remaining.AddRange(_allCards);
            Shuffle();
        }

        /// <summary>
        /// Restore a partially-drawn cycle from save data. Ids no longer present in the
        /// content set are dropped (a card removed since the save was written); if that
        /// leaves nothing, a fresh cycle starts instead of a dead deck.
        /// </summary>
        public void RestoreCycle(IEnumerable<string> remainingCardIds, string lastDrawnCardId)
        {
            _lastDrawnCardId = lastDrawnCardId;
            _remaining.Clear();

            if (remainingCardIds != null)
            {
                foreach (var id in remainingCardIds)
                {
                    var card = FindById(id);
                    if (card != null) _remaining.Add(card);
                }
            }

            if (_remaining.Count == 0) ResetCycle();
        }

        /// <summary>
        /// Take the next card, reshuffling if the cycle is exhausted.
        /// Returns null only when there is no content at all.
        /// </summary>
        public LawCardDefinition Draw()
        {
            if (_allCards.Count == 0) return null;

            if (_remaining.Count == 0)
            {
                ResetCycle();
                AvoidRepeatAcrossCycleBoundary();
            }

            var drawn = _remaining[0];
            _remaining.RemoveAt(0);
            _lastDrawnCardId = drawn.CardId;
            return drawn;
        }

        /// <summary>Find a card in the content set by id, or null.</summary>
        public LawCardDefinition FindById(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return null;
            foreach (var card in _allCards)
                if (card.CardId == cardId) return card;
            return null;
        }

        // ── Private ───────────────────────────────────────────────────────────────

        private void AvoidRepeatAcrossCycleBoundary()
        {
            if (string.IsNullOrEmpty(_lastDrawnCardId)) return;
            if (_remaining.Count <= 1) return;              // no alternative exists
            if (_remaining[0].CardId != _lastDrawnCardId) return;

            int swapIndex = _rng.Next(1, _remaining.Count);
            (_remaining[0], _remaining[swapIndex]) = (_remaining[swapIndex], _remaining[0]);
        }

        private void Shuffle()
        {
            // Fisher-Yates.
            for (int i = _remaining.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (_remaining[i], _remaining[j]) = (_remaining[j], _remaining[i]);
            }
        }
    }
}
