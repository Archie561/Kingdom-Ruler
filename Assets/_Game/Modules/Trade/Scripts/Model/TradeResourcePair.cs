using System;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Trade
{
    /// <summary>
    /// The pairs that pay for each other's warehouse upgrades (GDD §7): Stone↔Wood,
    /// Metal↔Minerals, Leather↔Clay. A rule in code rather than config, by decision.
    /// </summary>
    public static class TradeResourcePair
    {
        /// <summary>The resource whose stock pays for <paramref name="type"/>'s paired upgrade.</summary>
        public static TradeResourceType PairedWith(TradeResourceType type)
        {
            switch (type)
            {
                case TradeResourceType.Stone:    return TradeResourceType.Wood;
                case TradeResourceType.Wood:     return TradeResourceType.Stone;
                case TradeResourceType.Metal:    return TradeResourceType.Minerals;
                case TradeResourceType.Minerals: return TradeResourceType.Metal;
                case TradeResourceType.Leather:  return TradeResourceType.Clay;
                case TradeResourceType.Clay:     return TradeResourceType.Leather;
                default: throw new ArgumentOutOfRangeException(nameof(type), type, "No pairing defined.");
            }
        }
    }
}
