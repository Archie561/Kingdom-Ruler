using System;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.RandomOccurrences.Domain
{
    public enum RandomOccurrenceEffectTarget
    {
        Gold,
        Characteristic,
        TradeResource
    }

    /// <summary>
    /// A single resource effect from an event choice.
    /// Positive = gain, negative = loss. Never targets crystals.
    /// </summary>
    [Serializable]
    public struct RandomOccurrenceOutcomeEffect
    {
        public RandomOccurrenceEffectTarget Target;
        public CharacteristicType CharacteristicType; // only used if Target == Characteristic
        public TradeResourceType TradeResourceType;   // only used if Target == TradeResource
        public float Amount; // positive = gain, negative = loss
        public bool IsPercentage; // if true, Amount is a percentage of current stored

        public static RandomOccurrenceOutcomeEffect GoldEffect(float amount)
            => new() { Target = RandomOccurrenceEffectTarget.Gold, Amount = amount };

        public static RandomOccurrenceOutcomeEffect CharacteristicEffect(CharacteristicType type, float points)
            => new() { Target = RandomOccurrenceEffectTarget.Characteristic, CharacteristicType = type, Amount = points };

        public static RandomOccurrenceOutcomeEffect TradeResourceEffect(TradeResourceType type, float amount, bool isPercentage = false)
            => new() { Target = RandomOccurrenceEffectTarget.TradeResource, TradeResourceType = type, Amount = amount, IsPercentage = isPercentage };
    }
}
