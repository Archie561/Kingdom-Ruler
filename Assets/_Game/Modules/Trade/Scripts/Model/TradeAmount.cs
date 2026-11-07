using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Trade
{
    /// <summary>A whole number of units of one trade resource: one line of an offer, or of a trade's result.</summary>
    public readonly struct TradeAmount
    {
        public readonly TradeResourceType Resource;
        public readonly int Units;

        public TradeAmount(TradeResourceType resource, int units)
        {
            Resource = resource;
            Units    = units;
        }

        public override string ToString() => $"{Units} {Resource}";
    }
}
