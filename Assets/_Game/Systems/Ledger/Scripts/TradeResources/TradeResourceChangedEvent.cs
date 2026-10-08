namespace KingdomRuler.Systems.Ledger
{
    public readonly struct TradeResourceChangedEvent
    {
        public TradeResourceType ResourceType { get; }
        public float NewAmount { get; }
        public float Delta { get; }

        public TradeResourceChangedEvent(TradeResourceType resourceType, float newAmount, float delta)
        {
            ResourceType = resourceType;
            NewAmount = newAmount;
            Delta = delta;
        }
    }
}
