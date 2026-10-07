namespace KingdomRuler.Systems.Ledger
{
    public readonly struct CharacteristicLeveledUp
    {
        public CharacteristicType CharacteristicType { get; }
        public int NewLevel { get; }

        public CharacteristicLeveledUp(CharacteristicType characteristicType, int newLevel)
        {
            CharacteristicType = characteristicType;
            NewLevel = newLevel;
        }
    }
}
