using System;
using KingdomRuler.Shared.Ledger;

namespace KingdomRuler.Modules.Events.Domain
{
    public enum EventEffectTarget
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
    public struct EventOutcomeEffect
    {
        public EventEffectTarget Target;
        public CharacteristicType CharacteristicType; // only used if Target == Characteristic
        public TradeResourceType TradeResourceType;   // only used if Target == TradeResource
        public float Amount; // positive = gain, negative = loss
        public bool IsPercentage; // if true, Amount is a percentage of current stored

        public static EventOutcomeEffect GoldEffect(float amount)
            => new() { Target = EventEffectTarget.Gold, Amount = amount };

        public static EventOutcomeEffect CharacteristicEffect(CharacteristicType type, float points)
            => new() { Target = EventEffectTarget.Characteristic, CharacteristicType = type, Amount = points };

        public static EventOutcomeEffect TradeResourceEffect(TradeResourceType type, float amount, bool isPercentage = false)
            => new() { Target = EventEffectTarget.TradeResource, TradeResourceType = type, Amount = amount, IsPercentage = isPercentage };
    }
}
