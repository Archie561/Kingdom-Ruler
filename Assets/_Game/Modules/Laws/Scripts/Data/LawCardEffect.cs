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
        public float Points;

        public LawCardEffect(CharacteristicType characteristic, float points)
        {
            Characteristic = characteristic;
            Points = points;
        }
    }
}
