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

        /// <summary>A characteristic's level in the characteristics popup. {0} = level.</summary>
        public const string CharacteristicLevel = "ui.characteristic.level";

        /// <summary>Title of the buy-up confirmation.</summary>
        public const string BuyUpTitle = "ui.buyup.title";

        /// <summary>Body of the buy-up confirmation. {0} = crystal cost, {1} = characteristic name, {2} = the level it reaches.</summary>
        public const string BuyUpBody = "ui.buyup.body";

        /// <summary>Added to the buy-up confirmation when the player cannot afford it.</summary>
        public const string NotEnoughCrystals = "ui.buyup.not_enough";

        /// <summary>Every key above, for <c>LawsContentTests</c>.</summary>
        public static readonly string[] AllKeys =
        {
            RefillTitle, RefillBody, CharacteristicLevel, BuyUpTitle, BuyUpBody, NotEnoughCrystals
        };
    }
}
