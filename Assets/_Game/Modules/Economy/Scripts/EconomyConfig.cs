using UnityEngine;

namespace KingdomRuler.Modules.Economy
{
    [CreateAssetMenu(fileName = "EconomyConfig", menuName = "Kingdom Ruler/Economy/Economy Config")]
    public sealed class EconomyConfig : ScriptableObject
    {
        [Tooltip("Maximum hours of gold production a business can store.")]
        public float MaxStorageHours = 24f;
    }
}
