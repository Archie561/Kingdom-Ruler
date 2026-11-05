using System;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Laws
{
    /// <summary>
    /// One characteristic change caused by a decision on a law card.
    /// Positive points add progress; negative points remove it.
    /// </summary>
    [Serializable]
    public struct LawCardEffect
    {
        public CharacteristicType Characteristic;
        public int Points;

        public LawCardEffect(CharacteristicType characteristic, int points)
        {
            Characteristic = characteristic;
            Points = points;
        }
    }
}
