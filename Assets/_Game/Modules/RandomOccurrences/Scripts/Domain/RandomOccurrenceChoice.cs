using System;

namespace KingdomRuler.Modules.RandomOccurrences.Domain
{
    [Serializable]
    public struct RandomOccurrenceChoice
    {
        public string ChoiceTextKey; // Localization key
        public RandomOccurrenceOutcomeEffect[] Effects;
    }
}
