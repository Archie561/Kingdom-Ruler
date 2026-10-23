using System;
using System.Collections.Generic;

namespace KingdomRuler.Modules.Laws
{
    /// <summary>
    /// The draw order of law cards: a shuffle bag. Every card appears once per cycle before any
    /// repeats, and a new cycle never opens with the card that closed the previous one.
    /// </summary>
    public sealed class LawDeck
    {
        private readonly List<LawCardDefinition> _allCards  = new();
        private readonly List<LawCardDefinition> _remaining = new();
        private readonly Random _rng;

        public LawDeck(Random rng)
        {
            _rng = rng ?? new Random();
        }

        /// <summary>Id of the most recently drawn card, across cycles.</summary>
        public string LastDrawnCardId { get; private set; }

        /// <summary>Ids still to be drawn this cycle, in draw order.</summary>
        public List<string> RemainingCardIds()
        {
            var ids = new List<string>(_remaining.Count);
            foreach (var card in _remaining) ids.Add(card.CardId);
            return ids;
        }

        /// <summary>Replace the card set and start a fresh, shuffled cycle. Null entries are skipped.</summary>
        public void SetContents(IEnumerable<LawCardDefinition> cards)
        {
            _allCards.Clear();
            if (cards != null)
                foreach (var card in cards)
                    if (card != null) _allCards.Add(card);

            StartNewCycle();
        }

        /// <summary>
        /// Resume a saved cycle. Ids no longer in the card set are skipped; if none survive, a fresh
        /// cycle starts instead.
        /// </summary>
        public void RestoreCycle(IEnumerable<string> remainingCardIds, string lastDrawnCardId)
        {
            LastDrawnCardId = lastDrawnCardId;
            _remaining.Clear();

            if (remainingCardIds != null)
                foreach (var id in remainingCardIds)
                {
                    var card = FindById(id);
                    if (card != null) _remaining.Add(card);
                }

            if (_remaining.Count == 0) StartNewCycle();
        }

        /// <summary>Take the next card, reshuffling when the cycle is exhausted. Null only when the set is empty.</summary>
        public LawCardDefinition Draw()
        {
            if (_allCards.Count == 0) return null;

            if (_remaining.Count == 0)
            {
                StartNewCycle();
                AvoidRepeatAcrossCycles();
            }

            var drawn = _remaining[0];
            _remaining.RemoveAt(0);
            LastDrawnCardId = drawn.CardId;
            return drawn;
        }

        public LawCardDefinition FindById(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return null;
            foreach (var card in _allCards)
                if (card.CardId == cardId) return card;
            return null;
        }

        private void StartNewCycle()
        {
            _remaining.Clear();
            _remaining.AddRange(_allCards);

            // Fisher–Yates.
            for (int i = _remaining.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (_remaining[i], _remaining[j]) = (_remaining[j], _remaining[i]);
            }
        }

        private void AvoidRepeatAcrossCycles()
        {
            if (_remaining.Count <= 1 || _remaining[0].CardId != LastDrawnCardId) return;

            int swapIndex = _rng.Next(1, _remaining.Count);
            (_remaining[0], _remaining[swapIndex]) = (_remaining[swapIndex], _remaining[0]);
        }
    }
}
