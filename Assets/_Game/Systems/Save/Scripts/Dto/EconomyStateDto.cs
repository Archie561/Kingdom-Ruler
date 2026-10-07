using System.Collections.Generic;
using Newtonsoft.Json;

namespace KingdomRuler.Systems.Save
{
    /// <summary>Persisted state of the Economy module (GDD §8).</summary>
    public sealed class EconomyStateDto
    {
        [JsonProperty("businesses")]
        public List<BusinessStateDto> Businesses { get; set; } = new();
    }
}
