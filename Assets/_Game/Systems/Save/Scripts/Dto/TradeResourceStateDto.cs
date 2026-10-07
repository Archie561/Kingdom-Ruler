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
        /// A <b>defensive bootstrap value</b>, not the source of truth.
        /// </summary>
        /// <remarks>
        /// Warehouse capacity is owned by <c>TradeStateDto.WarehouseLevels</c> and re-asserted
        /// through the upgrade curve when the Trade module loads, so a retuned curve reaches
        /// existing saves. This copy exists so the Ledger stays restorable on its own, without a
        /// Trade module — which is what keeps the Ledger's own save tests meaningful.
        /// </remarks>
        [JsonProperty("capacity")]
        public float Capacity { get; set; }

        // Removed in schema v5: regenRatePerSecond was always capacity ÷ 86400 (GDD §7's 24-hour
        // refill), so persisting it was a third copy of a fact already stored twice. The Ledger
        // derives it on load. Newtonsoft ignores the key in older saves.

        /// <summary>ISO-8601 UTC. Drives offline regen catch-up on load.</summary>
        [JsonProperty("lastUpdatedUtc")]
        public string LastUpdatedUtc { get; set; }
    }
}
