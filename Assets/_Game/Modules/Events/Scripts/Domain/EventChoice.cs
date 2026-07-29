using System;

namespace KingdomRuler.Modules.Events.Domain
{
    [Serializable]
    public struct EventChoice
    {
        public string ChoiceTextKey; // Localization key
        public EventOutcomeEffect[] Effects;
    }
}
