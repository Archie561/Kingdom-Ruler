using System.Collections.Generic;
using Newtonsoft.Json;

namespace KingdomRuler.Shared.Services
{
    /// <summary>
    /// Save data for the Trade module.
    /// </summary>
    /// <remarks>
    /// The offers themselves are persisted, not just the timer. Without them the offer list was
    /// regenerated on every launch, which made relaunching the app a free instant refresh and
    /// bypassed the crystal price the mechanic charges for exactly that.
    /// </remarks>
    public sealed class TradeStateDto
    {
        /// <summary>
        /// ISO-8601 UTC wall-clock time at which the offer list is next replaced.
        /// </summary>
        /// <remarks>
        /// A deadline rather than a last-settled stamp, so it stays correct across the app being
        /// closed for any length of time and the refresh phase survives a long absence — the same
        /// change Laws made in schema v4, for the same reason. It is also directly readable in
        /// the save file, where a last-checked stamp only means something if you also know the
        /// configured interval.
        /// </remarks>
        [JsonProperty("nextOfferRefreshDueUtc")]
        public string NextOfferRefreshDueUtc { get; set; }

        /// <summary>
        /// TradeResourceType name → warehouse upgrade level. <b>Authoritative for capacity.</b>
        /// </summary>
        /// <remarks>
        /// The ledger section also stores a per-resource capacity, but that is a defensive
        /// bootstrap value so the Ledger stays restorable on its own. Trade re-asserts capacity
        /// from these levels on load, which means a retuned curve takes effect on existing saves
        /// instead of being frozen at whatever was written when the player last upgraded.
        /// </remarks>
        [JsonProperty("warehouseLevels")]
        public Dictionary<string, int> WarehouseLevels { get; set; } = new();

        /// <summary>
        /// The offers currently on screen. Null or empty on a save from before this was
        /// persisted, in which case one fresh batch is generated on load.
        /// </summary>
        [JsonProperty("activeOffers")]
        public List<TradeOfferDto> ActiveOffers { get; set; } = new();
    }
}
