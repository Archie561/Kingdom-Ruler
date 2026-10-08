namespace KingdomRuler.Systems.Ledger
{
    public readonly struct CharacteristicLeveledUpEvent
    {
        public CharacteristicType CharacteristicType { get; }
        public int NewLevel { get; }

        public CharacteristicLeveledUpEvent(CharacteristicType characteristicType, int newLevel)
        {
            CharacteristicType = characteristicType;
            NewLevel = newLevel;
        }
    }
}
