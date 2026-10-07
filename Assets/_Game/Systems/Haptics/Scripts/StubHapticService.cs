using UnityEngine;

namespace KingdomRuler.Systems.Haptics
{
    /// <summary>
    /// No-op haptic service for Editor and platforms without haptic support.
    /// </summary>
    public sealed class StubHapticService : IHapticService
    {
        public void TriggerLight()
        {
            Debug.Log("[StubHapticService] TriggerLight");
        }

        public void TriggerMedium()
        {
            Debug.Log("[StubHapticService] TriggerMedium");
        }
    }
}
