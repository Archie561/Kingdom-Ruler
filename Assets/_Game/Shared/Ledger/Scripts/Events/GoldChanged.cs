namespace KingdomRuler.Shared.Ledger
{
    public readonly struct GoldChanged
    {
        public long NewAmount { get; }
        public long Delta { get; }

        public GoldChanged(long newAmount, long delta)
        {
            NewAmount = newAmount;
            Delta = delta;
        }
    }
}
