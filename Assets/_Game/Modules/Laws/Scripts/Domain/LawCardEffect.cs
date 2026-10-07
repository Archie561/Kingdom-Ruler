using System;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Laws.Domain
{
    /// <summary>
    /// A single characteristic change applied by accepting or rejecting a law card.
    /// Positive values add points; negative values reduce points.
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
