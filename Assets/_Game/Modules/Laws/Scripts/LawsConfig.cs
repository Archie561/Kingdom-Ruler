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

        [Header("Leveling Curve")]
        [Tooltip("Points required per level. Index 0 = level 1. Falls back to formula for levels beyond the array.")]
        public float[] LevelingCurve = new float[]
        {
            100f,   // Level 1
            140f,   // Level 2 (100 × 1.35^1 ≈ 135 → rounded to 140)
            180f,   // Level 3 (100 × 1.35^2 ≈ 182 → 180)
            250f,   // Level 4 (100 × 1.35^3 ≈ 246 → 250)
            330f,   // Level 5
            450f,   // Level 6
            600f,   // Level 7
            810f,   // Level 8
            1100f,  // Level 9
            1480f,  // Level 10
        };
    }
}
