using System.Collections.Generic;
using Newtonsoft.Json;

namespace KingdomRuler.Shared.Services
{
    /// <summary>
    /// Save data for the Laws module (schema v2).
    ///
    /// Only the active card and the remaining shuffle-bag deck are persisted.
    /// The "virtual queue" behind the active card is recovered from CardsReplenishing
    /// and LastReplenishCheckUtc on load; ProcessReplenishment() handles offline catch-up.
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

        /// <summary>ISO-8601 UTC timestamp of the last replenishment check.</summary>
        [JsonProperty("lastReplenishCheckUtc")]
        public string LastReplenishCheckUtc { get; set; }
    }
}
