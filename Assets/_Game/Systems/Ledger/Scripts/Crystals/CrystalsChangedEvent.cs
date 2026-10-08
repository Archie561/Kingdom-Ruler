namespace KingdomRuler.Systems.Ledger
{
    public readonly struct CrystalsChangedEvent
    {
        public int NewAmount { get; }
        public int Delta { get; }

        public CrystalsChangedEvent(int newAmount, int delta)
        {
            NewAmount = newAmount;
            Delta = delta;
        }
    }
}
