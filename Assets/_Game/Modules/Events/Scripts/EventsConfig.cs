using UnityEngine;

namespace KingdomRuler.Modules.Events
{
    [CreateAssetMenu(fileName = "EventsConfig", menuName = "Kingdom Ruler/Events/Events Config")]
    public sealed class EventsConfig : ScriptableObject
    {
        [Tooltip("Seconds between event spawn checks (real-time including offline).")]
        public float SpawnIntervalSeconds = 3600f; // 1 hour default

        [Tooltip("Maximum pending events in the mailbox.")]
        public int MaxPendingEvents = 5;
    }
}
