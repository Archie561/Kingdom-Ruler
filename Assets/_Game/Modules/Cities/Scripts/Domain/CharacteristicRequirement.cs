using System;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Cities.Domain
{
    [Serializable]
    public struct CharacteristicRequirement
    {
        public CharacteristicType Type;
        public int RequiredLevel;

        public CharacteristicRequirement(CharacteristicType type, int requiredLevel)
        {
            Type = type;
            RequiredLevel = requiredLevel;
        }
    }
}
