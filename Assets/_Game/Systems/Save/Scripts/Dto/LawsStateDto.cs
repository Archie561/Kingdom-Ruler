using System.Collections.Generic;
using Newtonsoft.Json;

namespace KingdomRuler.Systems.Save
{
    /// <summary>Save data for the Laws module.</summary>
    /// <remarks>
    /// Cards behind the shown one have no identity — a card is drawn only when it is shown — so
    /// the queue is saved as one card id, the deck's remaining cycle, a count and a deadline.
    /// </remarks>
    public sealed class LawsStateDto
    {
        /// <summary>CardId of the card currently shown to the player. Null if none.</summary>
        [JsonProperty("activeCardId")]
        public string ActiveCardId { get; set; }

        /// <summary>
        /// CardId of the last card that was drawn (from either cycle).
        /// Used to prevent the same card appearing back-to-back across a cycle boundary.
        /// </summary>
        [JsonProperty("lastDrawnCardId")]
        public string LastDrawnCardId { get; set; }

        /// <summary>
        /// CardIds still remaining in the current shuffle-bag cycle (not yet drawn).
        /// On load, these are restored so the cycle continues rather than restarting.
        /// </summary>
        [JsonProperty("remainingDeckCardIds")]
        public List<string> RemainingDeckCardIds { get; set; } = new();

        /// <summary>Number of replenishment timer slots currently running.</summary>
        [JsonProperty("cardsReplenishing")]
        public int CardsReplenishing { get; set; }

        /// <summary>
        /// ISO-8601 UTC time at which the next card matures; null when nothing is replenishing. A
        /// deadline rather than a remaining duration, so it stays correct across any absence.
        /// </summary>
        [JsonProperty("nextReplenishDueUtc")]
        public string NextReplenishDueUtc { get; set; }
    }
}
