using System;

namespace KingdomRuler.Modules.RandomOccurrences.Domain
{
    /// <summary>
    /// One of the two options an occurrence offers.
    /// </summary>
    /// <remarks>
    /// No text key here: the choice is identified by which slot it occupies on its
    /// <c>RandomOccurrenceDefinition</c>, so its key derives as <c>{OccurrenceId}.choiceA</c>
    /// / <c>.choiceB</c> — the struct itself knows neither the id nor the slot, which is
    /// exactly why the key cannot be a field on it.
    /// </remarks>
    [Serializable]
    public struct RandomOccurrenceChoice
    {
        public RandomOccurrenceOutcomeEffect[] Effects;
    }
}
