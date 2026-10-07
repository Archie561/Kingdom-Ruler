using System.Collections.Generic;
using Newtonsoft.Json;

namespace KingdomRuler.Systems.Save
{
    /// <summary>
    /// Persisted state of the KingdomLedger — the game's single source of truth for
    /// currencies, warehouses and characteristics.
    /// </summary>
    /// <remarks>
    /// Resources and characteristics are keyed by **enum name**, not ordinal, so reordering
    /// TradeResourceType or CharacteristicType cannot silently reassign a player's stock or
    /// levels. A name that no longer exists is skipped on load.
    /// </remarks>
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
}
