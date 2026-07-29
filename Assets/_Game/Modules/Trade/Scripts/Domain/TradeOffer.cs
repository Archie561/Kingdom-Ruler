using System.Collections.Generic;
using KingdomRuler.Shared.Ledger;

namespace KingdomRuler.Modules.Trade.Domain
{
    /// <summary>
    /// Profitability category for a trade offer.
    /// </summary>
    public enum TradeProfitability
    {
        Profitable,
        Neutral,
        Unprofitable
    }

    /// <summary>
    /// A single trade offer. Pure data, no Unity dependencies.
    /// </summary>
    public sealed class TradeOffer
    {
        public string Id { get; }
        public Dictionary<TradeResourceType, float> GiveResources { get; }
        public Dictionary<TradeResourceType, float> ReceiveResources { get; }
        public TradeProfitability Profitability { get; }

        public TradeOffer(
            string id,
            Dictionary<TradeResourceType, float> give,
            Dictionary<TradeResourceType, float> receive,
            TradeProfitability profitability)
        {
            Id = id;
            GiveResources = give;
            ReceiveResources = receive;
            Profitability = profitability;
        }

        /// <summary>Total units the player must give.</summary>
        public float TotalGive()
        {
            float sum = 0;
            foreach (var kvp in GiveResources) sum += kvp.Value;
            return sum;
        }

        /// <summary>Total units the player receives.</summary>
        public float TotalReceive()
        {
            float sum = 0;
            foreach (var kvp in ReceiveResources) sum += kvp.Value;
            return sum;
        }
    }
}
