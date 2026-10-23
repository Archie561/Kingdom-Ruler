namespace KingdomRuler.Modules.Laws.UI
{
    /// <summary>What <see cref="LawCardView"/> draws: one card's text, already localized.</summary>
    /// <remarks>
    /// Deliberately nothing about the card's effects. Everything here is shown before the player
    /// decides, and inferring a law's effect from its writing is the mechanic (GDD §6).
    /// </remarks>
    public readonly struct LawCardViewData
    {
        public readonly string Title;
        public readonly string FlavorText;

        public LawCardViewData(string title, string flavorText)
        {
            Title      = title;
            FlavorText = flavorText;
        }
    }
}
