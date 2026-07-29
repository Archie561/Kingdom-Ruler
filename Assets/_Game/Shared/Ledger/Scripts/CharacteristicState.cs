namespace KingdomRuler.Shared.Ledger
{
    /// <summary>
    /// Mutable state for a single characteristic. Owned exclusively by KingdomLedger.
    /// Level floor is permanent — once reached, points cannot push below that level's threshold.
    /// </summary>
    public sealed class CharacteristicState
    {
        public int Level { get; set; }
        public float PointsIntoCurrentLevel { get; set; }
        public int PermanentFloor { get; set; }

        public CharacteristicState()
        {
            Level = 1;
            PointsIntoCurrentLevel = 0f;
            PermanentFloor = 1;
        }
    }
}
