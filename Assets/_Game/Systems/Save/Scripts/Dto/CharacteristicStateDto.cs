using Newtonsoft.Json;

namespace KingdomRuler.Systems.Save
{
    /// <summary>
    /// One characteristic's saved state. Nested under <see cref="LedgerStateDto"/>, keyed by
    /// <c>CharacteristicType</c> name.
    /// </summary>
    public sealed class CharacteristicStateDto
    {
        [JsonProperty("level")]
        public int Level { get; set; }

        [JsonProperty("pointsIntoCurrentLevel")]
        [JsonConverter(typeof(WholeNumberJsonConverter))]
        public int PointsIntoCurrentLevel { get; set; }
    }
}
