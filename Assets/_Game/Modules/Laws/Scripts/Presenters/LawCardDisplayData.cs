namespace KingdomRuler.Modules.Laws.Presenters
{
    /// <summary>
    /// Everything the card View needs to render one law card, already turned into strings.
    /// </summary>
    /// <remarks>
    /// The View used to receive the <c>LawCardDefinition</c> asset itself and format the
    /// effect summaries in a MonoBehaviour. That put view-model work in the View
    /// (ARCHITECTURE.md §4.4 assigns it to the Presenter), left the formatting untestable,
    /// and meant the localization pass would have had to move it anyway.
    ///
    /// Mirrors <see cref="CharacteristicDisplayData"/>, which the bars already use.
    /// </remarks>
    public readonly struct LawCardDisplayData
    {
        /// <summary>Identifies the card when reporting a swipe back to the Presenter.</summary>
        public readonly string CardId;

        /// <summary>String Table key for the card title (rendered raw until localization).</summary>
        public readonly string TitleKey;

        /// <summary>String Table key for the flavor text (rendered raw until localization).</summary>
        public readonly string FlavorTextKey;

        /// <summary>Human-readable summary of what accepting does.</summary>
        public readonly string AcceptSummary;

        /// <summary>Human-readable summary of what rejecting does.</summary>
        public readonly string RejectSummary;

        public LawCardDisplayData(
            string cardId,
            string titleKey,
            string flavorTextKey,
            string acceptSummary,
            string rejectSummary)
        {
            CardId        = cardId;
            TitleKey      = titleKey;
            FlavorTextKey = flavorTextKey;
            AcceptSummary = acceptSummary;
            RejectSummary = rejectSummary;
        }
    }
}
