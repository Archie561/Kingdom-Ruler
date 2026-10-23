using UnityEngine;

namespace KingdomRuler.Modules.Laws
{
    /// <summary>Tunable parameters for the Laws mechanic. One asset in <c>ScriptableObjects/Config/</c>.</summary>
    [CreateAssetMenu(fileName = "LawsConfig", menuName = "Kingdom Ruler/Laws/Laws Config")]
    public sealed class LawsConfig : ScriptableObject
    {
        [Header("Card Queue")]
        [Tooltip("Maximum cards the player can hold at once.")]
        public int MaxHeldCards = 8;

        [Tooltip("Seconds for one card to replenish, in real time including while the app is closed.")]
        public float CardReplenishTimeSeconds = 120f;

        [Header("Crystal Costs")]
        [Tooltip("Crystals per missing card for an instant refill.")]
        public int CrystalCostPerRefill = 2;

        [Tooltip("Buy-up cost = ceil(pointsRemaining / this), minimum 1. Must be > 0.")]
        public float CrystalBuyUpDivisor = 20f;

        [Header("Card Content")]
        [Tooltip("Every card that can appear. Each card stays a separate asset in ScriptableObjects/LawCards/.")]
        public LawCardDefinition[] AllCards;
    }
}
