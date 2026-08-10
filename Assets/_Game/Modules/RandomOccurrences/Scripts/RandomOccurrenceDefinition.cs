using UnityEngine;
using KingdomRuler.Modules.RandomOccurrences.Domain;

namespace KingdomRuler.Modules.RandomOccurrences
{
    [CreateAssetMenu(fileName = "RandomOccurrence_New", menuName = "Kingdom Ruler/Random Occurrences/Random Occurrence Definition")]
    public sealed class RandomOccurrenceDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string OccurrenceId;
        public string TitleKey;       // Localization key
        public string DescriptionKey; // Localization key

        [Header("Selection")]
        [Tooltip("RNG weight for weighted random selection. Higher = more likely.")]
        public float Weight = 1f;

        [Header("Choices")]
        public RandomOccurrenceChoice ChoiceA;
        public RandomOccurrenceChoice ChoiceB;
    }
}
