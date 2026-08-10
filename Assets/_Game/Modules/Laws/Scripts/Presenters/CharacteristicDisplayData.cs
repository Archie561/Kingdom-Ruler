using KingdomRuler.Shared.Ledger;

namespace KingdomRuler.Modules.Laws.Presenters
{
    /// <summary>
    /// Read-only view-model for one characteristic row, computed by LawsPresenter each refresh.
    /// No floor marker — the bar shows only progress within the current level and resets on level-up.
    /// </summary>
    public readonly struct CharacteristicDisplayData
    {
        /// <summary>Which of the 6 characteristics this data represents.</summary>
        public readonly CharacteristicType Type;

        /// <summary>Current level (1-based).</summary>
        public readonly int Level;

        /// <summary>
        /// Progress through the current level as a fraction [0, 1].
        /// Resets to 0 on level-up.
        /// </summary>
        public readonly float ProgressFraction;

        /// <summary>Crystal cost to instantly finish this level.</summary>
        public readonly int BuyUpCost;

        /// <summary>Whether the player has enough crystals to afford the buy-up.</summary>
        public readonly bool CanAffordBuyUp;

        public CharacteristicDisplayData(
            CharacteristicType type,
            int level,
            float progressFraction,
            int buyUpCost,
            bool canAffordBuyUp)
        {
            Type = type;
            Level = level;
            ProgressFraction = progressFraction;
            BuyUpCost = buyUpCost;
            CanAffordBuyUp = canAffordBuyUp;
        }
    }
}
