using UnityEngine;
using KingdomRuler.Modules.RandomOccurrences.Domain;

namespace KingdomRuler.Modules.RandomOccurrences
{
    /// <summary>
    /// One authored occurrence (GDD §10).
    /// </summary>
    /// <remarks>
    /// Carries no localization-key fields, deliberately: keys are derived from
    /// <see cref="OccurrenceId"/> the way Laws derives a card's from its CardId, so there is
    /// nothing to hand-author and nothing that can drift from the asset that declares it
    /// (<c>ARCHITECTURE.md</c> §2). When this module gets a Presenter, it resolves
    /// <c>{OccurrenceId}.title</c>, <c>.description</c>, <c>.choiceA</c> and <c>.choiceB</c>
    /// from an occurrences String Table.
    /// </remarks>
    [CreateAssetMenu(fileName = "RandomOccurrence_New", menuName = "Kingdom Ruler/Random Occurrences/Random Occurrence Definition")]
    public sealed class RandomOccurrenceDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string OccurrenceId;

        [Header("Selection")]
        [Tooltip("RNG weight for weighted random selection. Higher = more likely.")]
        public float Weight = 1f;

        [Header("Choices")]
        public RandomOccurrenceChoice ChoiceA;
        public RandomOccurrenceChoice ChoiceB;
    }
}
