using Newtonsoft.Json;

namespace KingdomRuler.Systems.Save
{
    /// <summary>
    /// The root of the save file. One section per module that has state.
    /// </summary>
    /// <remarks>
    /// <para>Bump <see cref="CurrentSchemaVersion"/> whenever the shape changes, even additively.</para>
    ///
    /// <para><b>No migrations yet, by decision</b> (<c>ARCHITECTURE.md</c> §5): until the first
    /// external build every save is a developer's, so an unreadable one is discarded and a fresh
    /// game started. From that build on, record each bump here — what changed and how an older save
    /// reads — because that is what the first migration will be written from.</para>
    /// </remarks>
    public sealed class GameStateDto
    {
        public const int CurrentSchemaVersion = 9;

        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        [JsonProperty("ledger")]
        public LedgerStateDto Ledger { get; set; }

        [JsonProperty("laws")]
        public LawsStateDto Laws { get; set; }

        [JsonProperty("trade")]
        public TradeStateDto Trade { get; set; }

        [JsonProperty("economy")]
        public EconomyStateDto Economy { get; set; }
    }
}
