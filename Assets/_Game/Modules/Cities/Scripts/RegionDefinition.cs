using UnityEngine;

namespace KingdomRuler.Modules.Cities
{
    [CreateAssetMenu(fileName = "Region_New", menuName = "Kingdom Ruler/Cities/Region Definition")]
    public sealed class RegionDefinition : ScriptableObject
    {
        public string RegionId;
        public string RegionNameKey;
        public CityDefinition[] Cities;
    }
}
