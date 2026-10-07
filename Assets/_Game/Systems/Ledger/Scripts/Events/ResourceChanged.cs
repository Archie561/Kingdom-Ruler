namespace KingdomRuler.Systems.Ledger
{
    public readonly struct ResourceChanged
    {
        public TradeResourceType ResourceType { get; }
        public float NewAmount { get; }
        public float Delta { get; }

        public ResourceChanged(TradeResourceType resourceType, float newAmount, float delta)
        {
            ResourceType = resourceType;
            NewAmount = newAmount;
            Delta = delta;
        }
    }
}
