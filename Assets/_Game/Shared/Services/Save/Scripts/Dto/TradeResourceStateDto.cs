using Newtonsoft.Json;

namespace KingdomRuler.Shared.Services
{
    /// <summary>
    /// One warehouse's persisted state. Nested under <see cref="LedgerStateDto"/>,
    /// keyed by TradeResourceType name.
    /// </summary>
    public sealed class TradeResourceStateDto
    {
        [JsonProperty("amount")]
        public float Amount { get; set; }

        [JsonProperty("capacity")]
        public float Capacity { get; set; }

        [JsonProperty("regenRatePerSecond")]
        public float RegenRatePerSecond { get; set; }

        /// <summary>ISO-8601 UTC. Drives offline regen catch-up on load.</summary>
        [JsonProperty("lastUpdatedUtc")]
        public string LastUpdatedUtc { get; set; }
    }
}
