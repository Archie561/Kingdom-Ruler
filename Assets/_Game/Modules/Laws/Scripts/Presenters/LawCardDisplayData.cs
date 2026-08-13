namespace KingdomRuler.Modules.Laws.Presenters
{
    /// <summary>
    /// Everything the card View needs to render one law card, already turned into strings.
    /// </summary>
    /// <remarks>
    /// <para>The View used to receive the <c>LawCardDefinition</c> asset itself and format
    /// text in a MonoBehaviour. That put view-model work in the View (ARCHITECTURE.md §4.4
    /// assigns it to the Presenter), left the formatting untestable, and meant the
    /// localization pass would have had to move it anyway.</para>
    ///
    /// <para><b>There is deliberately no effect summary here.</b> The player is not told which
    /// characteristics a law moves, or by how much, before deciding — working that out from
    /// the flavor text is the mechanic (GDD §6). Anything added to this struct is shown
    /// *before* the swipe, so a field naming an effect would give the guess away; feedback
    /// about what actually changed belongs after resolution, on the bars.</para>
    ///
    /// <para>Mirrors <see cref="CharacteristicDisplayData"/>, which the bars already use.</para>
    /// </remarks>
    public readonly struct LawCardDisplayData
    {
        /// <summary>Identifies the card when reporting a swipe back to the Presenter.</summary>
        public readonly string CardId;

        /// <summary>Card title, already resolved into the active locale.</summary>
        public readonly string Title;

        /// <summary>Flavor text, already resolved into the active locale.</summary>
        public readonly string FlavorText;

        public LawCardDisplayData(
            string cardId,
            string title,
            string flavorText)
        {
            CardId     = cardId;
            Title      = title;
            FlavorText = flavorText;
        }
    }
}
