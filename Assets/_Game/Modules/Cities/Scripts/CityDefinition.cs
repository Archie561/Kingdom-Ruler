using UnityEngine;
using KingdomRuler.Modules.Cities.Domain;

namespace KingdomRuler.Modules.Cities
{
    [CreateAssetMenu(fileName = "City_New", menuName = "Kingdom Ruler/Cities/City Definition")]
    public sealed class CityDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string CityId;
        public string RegionId;
        public string DisplayNameKey;
        public string DescriptionKey;
        public int PopulationGranted = 100;

        [Header("Purchase Requirements")]
        public CharacteristicRequirement[] CharacteristicRequirements;
        public ResourceCost[] TradeResourceCosts;
        public long GoldCost;
    }
}
