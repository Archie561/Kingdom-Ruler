namespace KingdomRuler.Systems.Ledger
{
    public readonly struct RegionCompleted
    {
        public string RegionId { get; }

        public RegionCompleted(string regionId)
        {
            RegionId = regionId;
        }
    }
}
