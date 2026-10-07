using System.Collections.Generic;
using Newtonsoft.Json;

namespace KingdomRuler.Systems.Save
{
    public sealed class CitiesStateDto
    {
        [JsonProperty("purchasedCityIds")]
        public List<string> PurchasedCityIds { get; set; } = new();

        [JsonProperty("totalPopulation")]
        public int TotalPopulation { get; set; }
    }
}
