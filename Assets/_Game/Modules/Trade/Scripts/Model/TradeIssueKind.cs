namespace KingdomRuler.Modules.Trade
{
    /// <summary>Why an offer cannot be taken, or what taking it will cost the player.</summary>
    public enum TradeIssueKind
    {
        /// <summary>The player holds too little of a resource the offer asks for. Blocks the trade.</summary>
        InsufficientResource,

        /// <summary>The offer was refreshed away or already taken. Blocks the trade.</summary>
        OfferUnavailable,

        /// <summary>A received resource will not all fit its warehouse. A warning only — the player may accept and lose the excess.</summary>
        WouldOverflowWarehouse
    }
}
