using UnityEngine;
using UnityEngine.Serialization;

namespace KingdomRuler.Modules.RandomOccurrences
{
    /// <summary>
    /// Tunable parameters for the Random Occurrences mechanic (GDD §10).
    /// One asset in ScriptableObjects/Config/.
    /// </summary>
    [CreateAssetMenu(fileName = "RandomOccurrenceConfig", menuName = "Kingdom Ruler/Random Occurrences/Random Occurrence Config")]
    public sealed class RandomOccurrenceConfig : ScriptableObject
    {
        [Tooltip("Seconds between occurrence spawn checks (real-time, including while the app is closed).")]
        public float SpawnIntervalSeconds = 3600f; // 1 hour default

        // Renamed from MaxPendingEvents in the Events → RandomOccurrences rename.
        // Serialized data is keyed by field name, so without this the value stored in
        // the existing asset would be dropped and silently replaced by the default.
        [FormerlySerializedAs("MaxPendingEvents")]
        [Tooltip("Maximum occurrences waiting in the mailbox at once.")]
        public int MaxPendingOccurrences = 5;
    }
}
