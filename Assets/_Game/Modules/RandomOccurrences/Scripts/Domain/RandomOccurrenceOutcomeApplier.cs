using KingdomRuler.Shared.Ledger;

namespace KingdomRuler.Modules.RandomOccurrences.Domain
{
    public static class RandomOccurrenceOutcomeApplier
    {
        /// <summary>
        /// Apply an array of effects to the ledger.
        /// Characteristic reductions respect the permanent level floor.
        ///
        /// Characteristic points level at whatever rate the Ledger's own curve dictates —
        /// identical to Laws. This used to take a points-required delegate, and the caller
        /// passed a flat 100/level stub, which silently made occurrences level a
        /// characteristic at a different rate than law cards did.
        /// </summary>
        public static void Apply(KingdomLedger ledger, RandomOccurrenceOutcomeEffect[] effects)
        {
            if (effects == null) return;
            foreach (var effect in effects)
            {
                switch (effect.Target)
                {
                    case RandomOccurrenceEffectTarget.Gold:
                        if (effect.Amount >= 0)
                            ledger.AddGold((long)effect.Amount);
                        else
                            ledger.SpendGold((long)(-effect.Amount));
                        break;

                    case RandomOccurrenceEffectTarget.Characteristic:
                        if (effect.Amount >= 0)
                            ledger.AddCharacteristicPoints(effect.CharacteristicType, effect.Amount);
                        else
                            ledger.ReduceCharacteristicPoints(effect.CharacteristicType, -effect.Amount);
                        break;

                    case RandomOccurrenceEffectTarget.TradeResource:
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
