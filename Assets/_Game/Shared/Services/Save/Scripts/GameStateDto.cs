using Newtonsoft.Json;

namespace KingdomRuler.Shared.Services
{
    /// <summary>
    /// Root save data transfer object. Versioned from day one to support
    /// forward-compatible migrations.
    /// </summary>
    public sealed class GameStateDto
    {
        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        // Sub-DTOs will be added here as modules are built:
        [JsonProperty("ledger")]
        public LedgerStateDto Ledger { get; set; }

        [JsonProperty("laws")]
        public LawsStateDto Laws { get; set; }

        [JsonProperty("trade")]
        public TradeStateDto Trade { get; set; }

        [JsonProperty("economy")]
        public EconomyStateDto Economy { get; set; }

        [JsonProperty("cities")]
        public CitiesStateDto Cities { get; set; }

        [JsonProperty("events")]
        public EventsStateDto Events { get; set; }
    }
}
