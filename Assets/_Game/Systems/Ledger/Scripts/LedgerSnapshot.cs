using System.Collections.Generic;

namespace KingdomRuler.Systems.Ledger
{
    /// <summary>An immutable copy of the Ledger's amounts and levels at one moment.</summary>
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
