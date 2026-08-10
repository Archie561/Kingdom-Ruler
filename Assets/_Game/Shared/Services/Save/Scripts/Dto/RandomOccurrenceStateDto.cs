using System.Collections.Generic;
using Newtonsoft.Json;

namespace KingdomRuler.Shared.Services
{
    public sealed class RandomOccurrenceStateDto
    {
        [JsonProperty("lastSpawnCheckUtc")]
        public string LastSpawnCheckUtc { get; set; }

        [JsonProperty("pendingOccurrenceIds")]
        public List<string> PendingOccurrenceIds { get; set; } = new();
    }
}
