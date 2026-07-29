namespace KingdomRuler.Shared.Ledger
{
    public readonly struct CrystalsChanged
    {
        public int NewAmount { get; }
        public int Delta { get; }

        public CrystalsChanged(int newAmount, int delta)
        {
            NewAmount = newAmount;
            Delta = delta;
        }
    }
}
