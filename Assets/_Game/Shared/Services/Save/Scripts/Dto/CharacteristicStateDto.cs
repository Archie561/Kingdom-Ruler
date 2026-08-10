using Newtonsoft.Json;

namespace KingdomRuler.Shared.Services
{
    /// <summary>
    /// One characteristic's persisted state. Nested under <see cref="LedgerStateDto"/>,
    /// keyed by CharacteristicType name.
    /// </summary>
    public sealed class CharacteristicStateDto
    {
        [JsonProperty("level")]
        public int Level { get; set; }

        [JsonProperty("pointsIntoCurrentLevel")]
        public float PointsIntoCurrentLevel { get; set; }

        // "permanentFloor" was removed in schemaVersion 2. It always equalled Level, so
        // nothing is lost: a v1 save's floor is recoverable as its level. Newtonsoft
        // ignores the now-unknown key on load, so v1 saves still deserialize cleanly.
    }
}
