using System.Collections.Generic;
using Newtonsoft.Json;

namespace KingdomRuler.Shared.Services
{
    /// <summary>Persisted state of the Economy module (GDD §8).</summary>
    public sealed class EconomyStateDto
    {
        [JsonProperty("businesses")]
        public List<BusinessStateDto> Businesses { get; set; } = new();
    }
}
