using System;
using KingdomRuler.Shared.Ledger;

namespace KingdomRuler.Modules.Events.Domain
{
    public static class EventOutcomeApplier
    {
        /// <summary>
        /// Apply an array of effects to the ledger.
        /// Characteristic reductions respect the permanent level floor.
        /// </summary>
        public static void Apply(KingdomLedger ledger, EventOutcomeEffect[] effects, Func<int, float> pointsRequired)
        {
            if (effects == null) return;
            foreach (var effect in effects)
            {
                switch (effect.Target)
                {
                    case EventEffectTarget.Gold:
                        if (effect.Amount >= 0)
                            ledger.AddGold((long)effect.Amount);
                        else
                            ledger.SpendGold((long)(-effect.Amount));
                        break;

                    case EventEffectTarget.Characteristic:
                        if (effect.Amount >= 0)
                            ledger.AddCharacteristicPoints(effect.CharacteristicType, effect.Amount, pointsRequired);
                        else
                            ledger.ReduceCharacteristicPoints(effect.CharacteristicType, -effect.Amount, pointsRequired);
                        break;

                    case EventEffectTarget.TradeResource:
                        float amount = effect.Amount;
                        if (effect.IsPercentage)
                        {
                            var state = ledger.GetTradeResource(effect.TradeResourceType);
                            amount = state.Amount * (effect.Amount / 100f);
                        }
                        if (amount >= 0)
                            ledger.AddTradeResource(effect.TradeResourceType, amount);
                        else
                            ledger.SpendTradeResource(effect.TradeResourceType, -amount);
                        break;
                }
            }
        }
    }
}
