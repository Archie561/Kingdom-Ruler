using UnityEngine;
using KingdomRuler.Modules.Events.Domain;

namespace KingdomRuler.Modules.Events
{
    [CreateAssetMenu(fileName = "EventDefinition_New", menuName = "Kingdom Ruler/Events/Event Definition")]
    public sealed class EventDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string EventId;
        public string TitleKey;       // Localization key
        public string DescriptionKey; // Localization key

        [Header("Selection")]
        [Tooltip("RNG weight for weighted random selection. Higher = more likely.")]
        public float Weight = 1f;

        [Header("Choices")]
        public EventChoice ChoiceA;
        public EventChoice ChoiceB;
    }
}
