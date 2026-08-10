namespace KingdomRuler.Shared.Ledger
{
    /// <summary>
    /// Mutable state for a single characteristic. Owned exclusively by KingdomLedger.
    /// </summary>
    /// <remarks>
    /// The permanent level floor from GDD §6 is implicit: <see cref="Level"/> is never
    /// decremented, so the floor is always the current level. It is deliberately not a
    /// separate field — one used to exist and was provably always equal to
    /// <see cref="Level"/>, which made the level-drop branch that read it unreachable.
    /// </remarks>
    public sealed class CharacteristicState
    {
        public int Level { get; set; }
        public float PointsIntoCurrentLevel { get; set; }

        public CharacteristicState()
        {
            Level = 1;
            PointsIntoCurrentLevel = 0f;
        }
    }
}
