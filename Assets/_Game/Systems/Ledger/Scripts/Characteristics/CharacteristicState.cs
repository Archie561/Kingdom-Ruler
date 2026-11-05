namespace KingdomRuler.Systems.Ledger
{
    /// <summary>
    /// State of one characteristic. Read-only outside <c>KingdomRuler.Systems</c>: every change goes
    /// through <see cref="KingdomLedger"/>.
    /// </summary>
    /// <remarks>
    /// There is no "permanent level floor" field: <see cref="Level"/> never decreases, so the floor
    /// from GDD §6 is always the current level.
    /// </remarks>
    public sealed class CharacteristicState
    {
        public int Level { get; internal set; }
        public float PointsIntoCurrentLevel { get; internal set; }

        public CharacteristicState()
        {
            Level = 1;
            PointsIntoCurrentLevel = 0f;
        }
    }
}
