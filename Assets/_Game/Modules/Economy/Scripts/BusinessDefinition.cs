using UnityEngine;

namespace KingdomRuler.Modules.Economy
{
    [CreateAssetMenu(fileName = "Business_New", menuName = "Kingdom Ruler/Economy/Business Definition")]
    public sealed class BusinessDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string BusinessId;
        public string DisplayNameKey; // Localization key
        public string DescriptionKey;

        [Header("Economics")]
        public long BaseCost = 100;
        public float CostMultiplier = 1.15f;
        public long BaseGoldPerMinute = 10;
    }
}
