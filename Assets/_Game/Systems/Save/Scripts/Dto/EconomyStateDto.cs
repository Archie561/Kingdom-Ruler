using System.Collections.Generic;
using Newtonsoft.Json;

namespace KingdomRuler.Systems.Save
{
    /// <summary>
    /// Save data for the Economy module: one entry per business type.
    /// </summary>
    /// <remarks>
    /// Keyed by <c>BusinessId</c>, the id authored on each business asset, so reordering the business
    /// list cannot reassign a player's units. An id that no longer exists is skipped on load, and a
    /// business absent from the save starts with nothing.
    /// </remarks>
    public sealed class EconomyStateDto
    {
        [JsonProperty("businesses")]
        public Dictionary<string, BusinessStateDto> Businesses { get; set; } = new();
    }
}
