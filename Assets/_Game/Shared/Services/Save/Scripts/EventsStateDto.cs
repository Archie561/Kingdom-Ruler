using System.Collections.Generic;
using Newtonsoft.Json;

namespace KingdomRuler.Shared.Services
{
    public sealed class EventsStateDto
    {
        [JsonProperty("lastSpawnCheckUtc")]
        public string LastSpawnCheckUtc { get; set; }

        [JsonProperty("pendingEventIds")]
        public List<string> PendingEventIds { get; set; } = new();
    }
}
