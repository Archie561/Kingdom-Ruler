using System.Collections.Generic;
using Newtonsoft.Json;

namespace KingdomRuler.Shared.Services
{
    /// <summary>
    /// Save data for Trade module state.
    /// </summary>
    public sealed class TradeStateDto
    {
        [JsonProperty("lastOfferRefreshUtc")]
        public string LastOfferRefreshUtc { get; set; }

        [JsonProperty("warehouseLevels")]
        public Dictionary<string, int> WarehouseLevels { get; set; } = new();
    }
}
