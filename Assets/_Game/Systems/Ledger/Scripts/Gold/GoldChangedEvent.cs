namespace KingdomRuler.Systems.Ledger
{
    public readonly struct GoldChangedEvent
    {
        public long NewAmount { get; }
        public long Delta { get; }

        public GoldChangedEvent(long newAmount, long delta)
        {
            NewAmount = newAmount;
            Delta = delta;
        }
    }
}
