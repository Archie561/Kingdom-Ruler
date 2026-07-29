using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace KingdomRuler.Shared.Services
{
    /// <summary>
    /// Save/load data transfer object for KingdomLedger state.
    /// </summary>
    public sealed class LedgerStateDto
    {
        [JsonProperty("gold")]
        public long Gold { get; set; }

        [JsonProperty("crystals")]
        public int Crystals { get; set; }

        [JsonProperty("tradeResources")]
        public Dictionary<string, TradeResourceStateDto> TradeResources { get; set; }
            = new Dictionary<string, TradeResourceStateDto>();

        [JsonProperty("characteristics")]
        public Dictionary<string, CharacteristicStateDto> Characteristics { get; set; }
            = new Dictionary<string, CharacteristicStateDto>();
    }

    public sealed class TradeResourceStateDto
    {
        [JsonProperty("amount")]
        public float Amount { get; set; }

        [JsonProperty("capacity")]
        public float Capacity { get; set; }

        [JsonProperty("regenRatePerSecond")]
        public float RegenRatePerSecond { get; set; }

        [JsonProperty("lastUpdatedUtc")]
        public string LastUpdatedUtc { get; set; }
    }

    public sealed class CharacteristicStateDto
    {
        [JsonProperty("level")]
        public int Level { get; set; }

        [JsonProperty("pointsIntoCurrentLevel")]
        public float PointsIntoCurrentLevel { get; set; }

        [JsonProperty("permanentFloor")]
        public int PermanentFloor { get; set; }
    }
}
