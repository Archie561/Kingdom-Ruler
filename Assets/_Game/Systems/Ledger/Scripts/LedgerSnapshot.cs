using System.Collections.Generic;

namespace KingdomRuler.Systems.Ledger
{
    /// <summary>
    /// Immutable read-only copy of the Ledger state for afford-checks and display.
    /// Prevents mutation during a frame/calculation.
    /// </summary>
    public sealed class LedgerSnapshot
    {
        public long Gold { get; }
        public int Crystals { get; }
        public IReadOnlyDictionary<TradeResourceType, float> TradeResourceAmounts { get; }
        public IReadOnlyDictionary<CharacteristicType, int> CharacteristicLevels { get; }

        public LedgerSnapshot(
            long gold,
            int crystals,
            Dictionary<TradeResourceType, float> tradeResourceAmounts,
            Dictionary<CharacteristicType, int> characteristicLevels)
        {
            Gold = gold;
            Crystals = crystals;
            TradeResourceAmounts = tradeResourceAmounts;
            CharacteristicLevels = characteristicLevels;
        }
    }
}
