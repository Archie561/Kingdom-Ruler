using UnityEngine;

namespace KingdomRuler.Systems.Haptics
{
    /// <summary>Logs each call instead of vibrating, until a device implementation exists.</summary>
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
