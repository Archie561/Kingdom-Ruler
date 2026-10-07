using Newtonsoft.Json;

namespace KingdomRuler.Systems.Save
{
    /// <summary>
    /// One owned business type's persisted state. Nested under <see cref="EconomyStateDto"/>.
    /// </summary>
    public sealed class BusinessStateDto
    {
        [JsonProperty("businessId")]
        public string BusinessId { get; set; }

        [JsonProperty("countOwned")]
        public int CountOwned { get; set; }

        [JsonProperty("storedGold")]
        public long StoredGold { get; set; }

        /// <summary>ISO-8601 UTC. Drives offline gold accrual on load.</summary>
        [JsonProperty("lastAccruedUtc")]
        public string LastAccruedUtc { get; set; }
    }
}
