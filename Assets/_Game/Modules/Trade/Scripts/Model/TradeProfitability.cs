namespace KingdomRuler.Modules.Trade
{
    /// <summary>
    /// How good a deal an offer is. All six resources are valued one unit to one unit (GDD §7), so
    /// this is decided by the units received against the units given and nothing else.
    /// </summary>
    public enum TradeProfitability
    {
        Profitable,
        Neutral,
        Unprofitable
    }
}
