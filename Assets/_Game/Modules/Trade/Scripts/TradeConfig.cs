using UnityEngine;

namespace KingdomRuler.Modules.Trade
{
    /// <summary>
    /// Tunable parameters for the Trade mechanic.
    /// One asset in ScriptableObjects/Config/.
    /// </summary>
    [CreateAssetMenu(fileName = "TradeConfig", menuName = "Kingdom Ruler/Trade/Trade Config")]
    public sealed class TradeConfig : ScriptableObject
    {
        [Header("Trade Offers")]
        [Tooltip("Number of active trade offers at a time.")]
        public int OfferCount = 10;

        [Tooltip("Auto-refresh interval in seconds (real-time including offline). Default: 1200 = 20 min.")]
        public float OfferRefreshTimeSeconds = 1200f;

        [Tooltip("Crystal cost for instant offer refresh.")]
        public int InstantRefreshCrystalCost = 3;

        [Tooltip("Base resource amount per offer for generation.")]
        public float OfferBaseAmount = 50f;

        [Header("Warehouse Upgrades")]
        [Tooltip("Capacity per upgrade level. Index 0 = base level. Designer-editable.")]
        public float[] WarehouseCapacityCurve = new float[]
        {
            100f,   // Level 0 (base)
            150f,   // Level 1
            225f,   // Level 2
            340f,   // Level 3
            500f,   // Level 4
            750f,   // Level 5
        };

        [Tooltip("Crystal cost per warehouse upgrade level. Index = current level.")]
        public int[] WarehouseCrystalCostCurve = new int[]
        {
            5,   // Level 0 -> 1
            8,   // Level 1 -> 2
            12,  // Level 2 -> 3
            18,  // Level 3 -> 4
            25,  // Level 4 -> 5
        };
    }
}
