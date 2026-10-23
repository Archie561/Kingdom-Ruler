namespace KingdomRuler.Modules.Laws.UI
{
    /// <summary>
    /// Laws text built in code with no data asset behind it. Public so <c>LawsContentTests</c>
    /// checks the String Table through these same constants.
    /// </summary>
    public static class LawsUIText
    {
        public const string StringTable = "LawsUITable";

        /// <summary>Title of the refill confirmation.</summary>
        public const string RefillTitle = "ui.refill.title";

        /// <summary>Body of the refill confirmation. {0} = crystal cost, {1} = time until the queue refills on its own.</summary>
        public const string RefillBody = "ui.refill.body";
    }
}
