using System.Collections.Generic;
using Newtonsoft.Json;

namespace KingdomRuler.Systems.Save
{
    /// <summary>
    /// Save data for the Laws module.
    ///
    /// Only the active card and the remaining shuffle-bag deck are persisted. The queue
    /// behind the active card has no card identities — cards are drawn when shown, not
    /// when their timer matures — so it is recovered from a count plus a deadline, and
    /// ProcessReplenishment() handles offline catch-up on load.
    /// </summary>
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
        /// ISO-8601 UTC wall-clock time at which the next card matures. Null/empty when
        /// nothing is replenishing.
        /// </summary>
        /// <remarks>
        /// A deadline rather than a remaining duration, so it stays correct across the app
        /// being closed for any length of time. It is also directly readable in the save
        /// file — "the next card lands at 20:58" — where a last-checked stamp only means
        /// something if you also know the configured interval.
        /// </remarks>
        [JsonProperty("nextReplenishDueUtc")]
        public string NextReplenishDueUtc { get; set; }
    }
}
