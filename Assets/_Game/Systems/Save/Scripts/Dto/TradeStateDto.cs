using System.Collections.Generic;
using Newtonsoft.Json;

namespace KingdomRuler.Systems.Save
{
    /// <summary>
    /// Save data for the Trade module.
    /// </summary>
    /// <remarks>
    /// The offers themselves are saved, not just the timer, so relaunching the app cannot reroll
    /// them for free and bypass the crystal-priced refresh (GDD §7).
    /// </remarks>
    public sealed class TradeStateDto
    {
        /// <summary>
        /// ISO-8601 UTC wall-clock time at which the offer list is next replaced.
        /// </summary>
        /// <remarks>
        /// A deadline rather than a last-settled stamp, so it stays correct across any absence
        /// (<c>ARCHITECTURE.md</c> §4.5).
        /// </remarks>
        [JsonProperty("nextOfferRefreshDueUtc")]
        public string NextOfferRefreshDueUtc { get; set; }

        /// <summary>
        /// TradeResourceType name → warehouse upgrade level. <b>Authoritative for capacity.</b>
        /// </summary>
        /// <remarks>
        /// The ledger section's per-resource capacity is only a fallback. Trade re-derives capacity
        /// from these levels on load, so a retuned curve takes effect on existing saves.
        /// </remarks>
        [JsonProperty("warehouseLevels")]
        public Dictionary<string, int> WarehouseLevels { get; set; } = new();

        /// <summary>The offers currently on screen. Null or empty: one fresh batch is generated on load.</summary>
        [JsonProperty("activeOffers")]
        public List<TradeOfferDto> ActiveOffers { get; set; } = new();
    }
}
