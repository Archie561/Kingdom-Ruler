using System.Collections.Generic;
using Newtonsoft.Json;

namespace KingdomRuler.Shared.Services
{
    public sealed class EconomyStateDto
    {
        [JsonProperty("businesses")]
        public List<BusinessStateDto> Businesses { get; set; } = new();
    }

    public sealed class BusinessStateDto
    {
        [JsonProperty("businessId")]
        public string BusinessId { get; set; }

        [JsonProperty("countOwned")]
        public int CountOwned { get; set; }

        [JsonProperty("storedGold")]
        public long StoredGold { get; set; }

        [JsonProperty("lastAccruedUtc")]
        public string LastAccruedUtc { get; set; }
    }
}
