namespace KingdomRuler.Shared.Ledger
{
    public readonly struct CityPurchased
    {
        public string CityId { get; }
        public int NewPopulation { get; }

        public CityPurchased(string cityId, int newPopulation)
        {
            CityId = cityId;
            NewPopulation = newPopulation;
        }
    }
}
