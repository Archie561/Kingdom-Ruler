using KingdomRuler.Shared.Ledger;

namespace KingdomRuler.Modules.Laws.Domain
{
    /// <summary>
    /// Translates a law card's authored effects into Ledger operations.
    /// </summary>
    /// <remarks>
    /// The sign convention is the whole job: a card authors a single signed number per
    /// characteristic, while the Ledger deliberately splits gains and losses into separate
    /// methods so a caller cannot accidentally subtract by passing a negative. This is the
    /// one place that translation happens — the mirror of
    /// RandomOccurrenceOutcomeApplier in the occurrences module.
    ///
    /// The leveling rate is not a concern here: it belongs to the Ledger (ARCHITECTURE.md §4.3).
    /// </remarks>
    public static class LawCardEffectApplier
    {
        public static void Apply(KingdomLedger ledger, LawCardEffect[] effects)
        {
            if (ledger == null || effects == null) return;

            foreach (var effect in effects)
            {
                if (effect.Points >= 0f)
                    ledger.AddCharacteristicPoints(effect.Characteristic, effect.Points);
                else
                    ledger.ReduceCharacteristicPoints(effect.Characteristic, -effect.Points);
            }
        }
    }
}
