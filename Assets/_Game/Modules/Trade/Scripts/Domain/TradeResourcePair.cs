using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Trade.Domain
{
    /// <summary>
    /// Defines the resource pairing for warehouse cross-upgrade costs.
    /// Stone↔Wood, Metal↔Minerals, Leather↔Clay.
    /// </summary>
    public static class TradeResourcePair
    {
        public static TradeResourceType GetPairedResource(TradeResourceType type)
        {
            return type switch
            {
                TradeResourceType.Stone => TradeResourceType.Wood,
                TradeResourceType.Wood => TradeResourceType.Stone,
                TradeResourceType.Metal => TradeResourceType.Minerals,
                TradeResourceType.Minerals => TradeResourceType.Metal,
                TradeResourceType.Leather => TradeResourceType.Clay,
                TradeResourceType.Clay => TradeResourceType.Leather,
                _ => throw new System.ArgumentException($"Unknown resource type: {type}", nameof(type))
            };
        }
    }
}
