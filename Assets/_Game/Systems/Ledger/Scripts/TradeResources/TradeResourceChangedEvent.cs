namespace KingdomRuler.Systems.Ledger
{
    /// <summary>A trade resource was added or spent. Passive regeneration does not raise it (<see cref="KingdomLedger.AccruePassiveResourceRegen"/>).</summary>
    public readonly struct TradeResourceChangedEvent
    {
        public TradeResourceType ResourceType { get; }
        public int NewAmount { get; }

        /// <summary>Positive when added, negative when spent.</summary>
        public int Delta { get; }

        public TradeResourceChangedEvent(TradeResourceType resourceType, int newAmount, int delta)
        {
            ResourceType = resourceType;
            NewAmount    = newAmount;
            Delta        = delta;
        }
    }
}
