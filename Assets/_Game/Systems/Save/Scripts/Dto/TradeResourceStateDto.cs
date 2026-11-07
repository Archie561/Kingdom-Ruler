using Newtonsoft.Json;

namespace KingdomRuler.Systems.Save
{
    /// <summary>
    /// One warehouse's persisted state. Nested under <see cref="LedgerStateDto"/>,
    /// keyed by TradeResourceType name.
    /// </summary>
    /// <remarks>
    /// Whole numbers, read through <see cref="WholeNumberJsonConverter"/> so a file holding
    /// <c>87.34</c> or <c>100.0</c> still loads (as 87 and 100) instead of being set aside as corrupt.
    /// </remarks>
    public sealed class TradeResourceStateDto
    {
        [JsonProperty("amount")]
        [JsonConverter(typeof(WholeNumberJsonConverter))]
        public int Amount { get; set; }

        /// <summary>
        /// A <b>fallback</b>, not the source of truth.
        /// </summary>
        /// <remarks>
        /// Capacity is owned by <see cref="TradeStateDto.WarehouseLevels"/> and re-derived through
        /// the upgrade curve when Trade loads, so a retuned curve reaches existing saves. This copy
        /// keeps the Ledger restorable on its own.
        /// </remarks>
        [JsonProperty("capacity")]
        [JsonConverter(typeof(WholeNumberJsonConverter))]
        public int Capacity { get; set; }

        /// <summary>ISO-8601 UTC: when the unit now regenerating started. Drives offline regen on load.</summary>
        [JsonProperty("lastUpdatedUtc")]
        public string LastUpdatedUtc { get; set; }
    }
}
