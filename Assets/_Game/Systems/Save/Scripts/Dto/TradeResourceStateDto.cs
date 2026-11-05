using Newtonsoft.Json;

namespace KingdomRuler.Systems.Save
{
    /// <summary>
    /// One warehouse's persisted state. Nested under <see cref="LedgerStateDto"/>,
    /// keyed by TradeResourceType name.
    /// </summary>
    public sealed class TradeResourceStateDto
    {
        [JsonProperty("amount")]
        public float Amount { get; set; }

        /// <summary>
        /// A <b>fallback</b>, not the source of truth.
        /// </summary>
        /// <remarks>
        /// Capacity is owned by <see cref="TradeStateDto.WarehouseLevels"/> and re-derived through
        /// the upgrade curve when Trade loads, so a retuned curve reaches existing saves. This copy
        /// keeps the Ledger restorable on its own. The regen rate is not saved: it always follows
        /// from capacity (GDD §7).
        /// </remarks>
        [JsonProperty("capacity")]
        public float Capacity { get; set; }

        /// <summary>ISO-8601 UTC. Drives offline regen catch-up on load.</summary>
        [JsonProperty("lastUpdatedUtc")]
        public string LastUpdatedUtc { get; set; }
    }
}
