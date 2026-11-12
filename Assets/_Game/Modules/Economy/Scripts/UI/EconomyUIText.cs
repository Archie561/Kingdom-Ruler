namespace KingdomRuler.Modules.Economy.UI
{
    /// <summary>
    /// Economy text built in code with no data asset behind it. Public so <c>EconomyContentTests</c>
    /// checks the String Table through these same constants. Fixed chrome (button captions, "Full")
    /// is not here: it is a <c>LocalizeStringEvent</c> on the prefab. Business names and flavor live in
    /// <c>BusinessesTable</c>, keyed by <see cref="BusinessDefinition"/>; the number suffixes in
    /// <c>SharedTable</c>, keyed by <c>NumberSuffixes</c>.
    /// </summary>
    public static class EconomyUIText
    {
        public const string StringTable = "EconomyUITable";

        // ── A business: its row and its panel ─────────────────────────────────────

        /// <summary>{0} = units owned, abbreviated.</summary>
        public const string UnitsOwned = "ui.units_owned";

        /// <summary>{0} = gold per hour, abbreviated.</summary>
        public const string Rate = "ui.rate";

        /// <summary>{0} = how many "Max" buys, abbreviated: as many as affordable, or 1 when none is.</summary>
        public const string BuyMax = "ui.buy_max";

        /// <summary>Shown instead of the buy prices at the units ceiling.</summary>
        public const string Maxed = "ui.maxed";

        // ── The crystal top-up ────────────────────────────────────────────────────

        public const string TopUpTitle = "ui.topup.title";

        /// <summary>{0} = gold short, {1} = units, {2} = business name, {3} = crystals — every number abbreviated.</summary>
        public const string TopUpBody = "ui.topup.body";

        /// <summary>Added to the top-up confirmation when the player cannot afford it; Confirm is inactive.</summary>
        public const string NotEnoughCrystals = "ui.not_enough_crystals";

        /// <summary>Every key above, for <c>EconomyContentTests</c>. A new code-resolved key goes here too.</summary>
        public static readonly string[] AllKeys =
        {
            UnitsOwned, Rate, BuyMax, Maxed,
            TopUpTitle, TopUpBody, NotEnoughCrystals
        };
    }
}
