using UnityEngine;
using KingdomRuler.Systems.Ledger;

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

        /// <summary>Display name, already resolved into the active locale.</summary>
        public readonly string Name;

        /// <summary>
        /// Icon for this characteristic, from the shared registry. Null is legitimate while
        /// art is outstanding — the View hides the slot rather than drawing an empty box.
        /// </summary>
        /// <remarks>
        /// The one <c>UnityEngine.Object</c> carried on a display struct, deliberately. The
        /// alternative is the View reading <c>CharacteristicRegistry</c> itself, which would
        /// hand it back the data dependency the MVP split exists to remove — the Presenter
        /// decides *what* is shown, the View decides *how*, and a sprite reference is the
        /// same kind of answer as a resolved string.
        /// </remarks>
        public readonly Sprite Icon;

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
            string name,
            Sprite icon,
            int level,
            float progressFraction,
            int buyUpCost,
            bool canAffordBuyUp)
        {
            Type = type;
            Name = name;
            Icon = icon;
            Level = level;
            ProgressFraction = progressFraction;
            BuyUpCost = buyUpCost;
            CanAffordBuyUp = canAffordBuyUp;
        }
    }
}
