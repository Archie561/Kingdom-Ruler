using UnityEngine;

namespace KingdomRuler.Modules.Laws
{
    /// <summary>
    /// Tunable parameters for the Laws mechanic.
    /// One asset in ScriptableObjects/Config/.
    /// </summary>
    [CreateAssetMenu(fileName = "LawsConfig", menuName = "Kingdom Ruler/Laws/Laws Config")]
    public sealed class LawsConfig : ScriptableObject
    {
        [Header("Card Queue")]
        [Tooltip("Maximum cards the player can hold at once.")]
        public int MaxHeldCards = 8;

        [Tooltip("Time in seconds for one card to replenish (real-time, including offline). Default: 120 = 2 minutes.")]
        public float CardReplenishTimeSeconds = 120f;

        [Header("Crystal Costs")]
        [Tooltip("Crystals spent per missing card when using instant refill.")]
        public int CrystalCostPerRefill = 2;

        [Tooltip("Divisor for crystal buy-up cost. Cost = ceil(pointsRemaining / this value), min 1.")]
        public float CrystalBuyUpDivisor = 20f;

        // NOTE: the characteristic leveling curve deliberately does NOT live here.
        // It is Ledger-owned (CharacteristicLevelingCurve in Systems/Ledger) because Laws is not the
        // only mechanic that awards characteristic points — see ARCHITECTURE.md §4.3.

        [Header("Card Content")]
        [Tooltip(
            "Every LawCardDefinition that can appear in the queue. Each card remains a " +
            "separate asset in ScriptableObjects/LawCards/ (ARCHITECTURE.md §7); this is " +
            "only the registry the manager builds its shuffle bag from.")]
        public LawCardDefinition[] AllCards;
    }
}
