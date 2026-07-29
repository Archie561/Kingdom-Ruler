using System.Collections.Generic;
using Newtonsoft.Json;

namespace KingdomRuler.Shared.Services
{
    /// <summary>
    /// Save data for Laws module state.
    /// </summary>
    public sealed class LawsStateDto
    {
        [JsonProperty("heldCardIds")]
        public List<string> HeldCardIds { get; set; } = new();

        [JsonProperty("cardsReplenishing")]
        public int CardsReplenishing { get; set; }

        [JsonProperty("lastReplenishCheckUtc")]
        public string LastReplenishCheckUtc { get; set; }
    }
}
