using System.Collections.Generic;
using Newtonsoft.Json;

namespace KingdomRuler.Systems.Save
{
    /// <summary>
    /// One persisted trade offer. Nested under <see cref="TradeStateDto"/>.
    /// </summary>
    /// <remarks>
    /// Resources are keyed by <b>enum name</b>, not ordinal, and profitability is stored by name
    /// too — so reordering <c>TradeResourceType</c> or <c>TradeProfitability</c> cannot silently
    /// reassign what an offer trades. The same convention <see cref="LedgerStateDto"/> documents.
    /// A name that no longer exists is skipped on load.
    /// </remarks>
    public sealed class TradeOfferDto
    {
        /// <summary>Stable id — what the player's tap resolves against.</summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>TradeProfitability member name.</summary>
        [JsonProperty("profitability")]
        public string Profitability { get; set; }

        /// <summary>TradeResourceType name → units the player gives.</summary>
        [JsonProperty("give")]
        public Dictionary<string, float> Give { get; set; } = new();

        /// <summary>TradeResourceType name → units the player receives.</summary>
        [JsonProperty("receive")]
        public Dictionary<string, float> Receive { get; set; } = new();
    }
}
