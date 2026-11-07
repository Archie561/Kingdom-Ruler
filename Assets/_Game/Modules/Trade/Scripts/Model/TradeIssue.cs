using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Trade
{
    /// <summary>
    /// One problem with an offer, naming the resource and the number behind it, so the confirmation
    /// can say exactly what is wrong (GDD §7). Turning it into a sentence is the Presenter's job.
    /// </summary>
    /// <remarks>
    /// <b>Overflow warns; it does not block</b> (<see cref="BlocksAcceptance"/>). "The warehouse is
    /// full" reads like a reason to refuse, which makes this the easiest rule here to undo by
    /// accident: only being unable to pay the give side stops a trade.
    /// </remarks>
    public readonly struct TradeIssue
    {
        public readonly TradeIssueKind Kind;

        /// <summary>The resource at fault. Meaningless for <see cref="TradeIssueKind.OfferUnavailable"/>.</summary>
        public readonly TradeResourceType Resource;

        /// <summary>The shortfall for a resource the player cannot pay; the units that will be lost for an overflow; 0 otherwise.</summary>
        public readonly int Units;

        public TradeIssue(TradeIssueKind kind, TradeResourceType resource, int units)
        {
            Kind     = kind;
            Resource = resource;
            Units    = units;
        }

        /// <summary>True for everything except an overflow, which the player may accept.</summary>
        public bool BlocksAcceptance => Kind != TradeIssueKind.WouldOverflowWarehouse;

        public static TradeIssue Unavailable() => new TradeIssue(TradeIssueKind.OfferUnavailable, default, 0);

        public override string ToString() => $"{Kind}({Resource}, {Units})";
    }
}
