namespace KingdomRuler.Modules.Trade.UI
{
    /// <summary>What an accepted trade gave the player: every received line, then every line lost to a full warehouse.</summary>
    public readonly struct TradeReceiptViewData
    {
        public readonly TradeReceiptLineViewData[] Lines;

        public TradeReceiptViewData(TradeReceiptLineViewData[] lines) => Lines = lines;
    }
}
