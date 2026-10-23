using System;
using System.Collections.Generic;

namespace KingdomRuler.Modules.Laws
{
    /// <summary>
    /// The player's hand of law cards: up to <see cref="MaxCount"/> held, exactly one shown.
    /// </summary>
    /// <remarks>
    /// <para><c>HeldCount + ReplenishingCount == MaxCount</c> always. Only the replenishing count
    /// is stored (in the timer); the held count is derived from it, so the two cannot disagree.</para>
    ///
    /// <para>Cards behind the shown one have no identity: a card is drawn from the deck only at
    /// the moment it is shown. That is why the save holds one active id plus the deck's cycle.</para>
    ///
    /// <para>Pure logic: it is handed the current time and never touches the Ledger.</para>
    /// </remarks>
    public sealed class LawQueue
    {
        private readonly LawsConfig         _config;
        private readonly LawDeck            _deck;
        private readonly ReplenishmentTimer _timer = new();

        public LawQueue(LawsConfig config, Random rng)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _deck   = new LawDeck(rng);
        }

        /// <summary>The card shown to the player, or null.</summary>
        public LawCardDefinition ActiveCard { get; private set; }

        public int MaxCount          => _config.MaxHeldCards;
        public int ReplenishingCount => _timer.PendingCount;

        /// <summary>Cards the player holds, the shown one included — the "6" in "6/8".</summary>
        public int HeldCount => Math.Max(0, MaxCount - ReplenishingCount);

        public string       LastDrawnCardId     => _deck.LastDrawnCardId;
        public List<string> RemainingDeckCardIds() => _deck.RemainingCardIds();
        public DateTime?    NextReplenishDueUtc => _timer.NextDueUtc;

        private float Interval => _config.CardReplenishTimeSeconds;

        /// <summary>A fresh game: the deck is shuffled and every slot starts replenishing.</summary>
        public void StartEmpty(DateTime now)
        {
            _deck.SetContents(_config.AllCards);
            _timer.Start(MaxCount, now, Interval);
        }

        public void Restore(string activeCardId, IEnumerable<string> remainingDeckCardIds,
                            string lastDrawnCardId, int replenishingCount, DateTime nextReplenishDueUtc)
        {
            _deck.SetContents(_config.AllCards);
            _deck.RestoreCycle(remainingDeckCardIds, lastDrawnCardId);
            ActiveCard = _deck.FindById(activeCardId);
            _timer.Restore(replenishingCount, nextReplenishDueUtc);

            // The saved card may have been removed from the game since; show a held card in its place.
            ShowNextIfIdle();
        }

        /// <summary>Settle replenishment up to <paramref name="now"/>. Returns whether any card matured.</summary>
        public bool Advance(DateTime now)
        {
            if (_timer.Advance(now, Interval) == 0) return false;
            ShowNextIfIdle();
            return true;
        }

        /// <summary>
        /// Remove the shown card, start replenishing its slot and show the next held card, if any.
        /// Returns the removed card, or null when none was shown.
        /// </summary>
        public LawCardDefinition TakeActiveCard(DateTime now)
        {
            var card = ActiveCard;
            if (card == null) return null;

            ActiveCard = null;
            if (ReplenishingCount < MaxCount) _timer.AddOne(now, Interval);
            ShowNextIfIdle();
            return card;
        }

        /// <summary>Mature every replenishing card at once.</summary>
        public void CompleteReplenishment()
        {
            _timer.CompleteAll();
            ShowNextIfIdle();
        }

        public float SecondsUntilNextCard(DateTime now) => _timer.SecondsUntilNext(now);
        public float SecondsUntilFull(DateTime now)     => _timer.SecondsUntilAll(now, Interval);

        private void ShowNextIfIdle()
        {
            if (ActiveCard == null && HeldCount > 0)
                ActiveCard = _deck.Draw();
        }
    }
}
