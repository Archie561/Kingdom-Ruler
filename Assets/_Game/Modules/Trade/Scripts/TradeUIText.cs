namespace KingdomRuler.Modules.Trade
{
    /// <summary>
    /// Owns the String Table and the key constants for Trade's screen text that is built in
    /// code — the blocker and warning messages, which take arguments and are chosen at runtime.
    /// </summary>
    /// <remarks>
    /// <para>Trade resolves several of its strings <em>from code</em>, because "You need 12 more
    /// Stone" is composed at runtime from an amount and a resource name. <c>ARCHITECTURE.md</c> §2
    /// says that is exactly the moment the keys type appears.</para>
    ///
    /// <para><b>Public, and a type of its own — not internal, not nested inside the Presenter.</b>
    /// <c>TradeTextTests</c> lives in the separate EditMode test assembly, so either of those
    /// would be invisible to it and the test would be forced back into re-typing the literals —
    /// which is the precise failure this shape prevents. A check that agrees with itself while
    /// disagreeing with the game passes, and leaves a blank message on screen.</para>
    ///
    /// <para>Fixed chrome — the panel titles, the column headings, the button captions — is NOT
    /// here. Those stay as <c>LocalizeStringEvent</c> components on the prefab, which also keeps
    /// them updating on a locale change without a Presenter round-trip.</para>
    /// </remarks>
    public static class TradeUIText
    {
        /// <summary>Trade's own chrome table. Resource names live in SharedTable instead.</summary>
        public const string StringTable = "TradeUITable";

        // ── Blockers and warnings (composed in TradePresenter) ────────────────────

        /// <summary>{0} = shortfall, {1} = resource name. The player cannot pay.</summary>
        public const string BlockerInsufficient = "ui.blocker_insufficient";

        /// <summary>{0} = resource name, {1} = units that will not fit. A warning, not a refusal.</summary>
        public const string WarningOverflow = "ui.warning_overflow";

        /// <summary>The offer was refreshed away before it could be confirmed.</summary>
        public const string BlockerUnavailable = "ui.blocker_unavailable";

        // ── Runtime-composed labels ───────────────────────────────────────────────

        /// <summary>{0} = amount, {1} = capacity.</summary>
        public const string WarehouseAmount = "ui.warehouse_amount";

        /// <summary>{0} = level.</summary>
        public const string WarehouseLevel = "ui.warehouse_level";

        /// <summary>{0} = crystal cost.</summary>
        public const string UpgradeCostCrystals = "ui.upgrade_cost_crystals";

        /// <summary>{0} = units, {1} = paired resource name.</summary>
        public const string UpgradeCostPaired = "ui.upgrade_cost_paired";

        /// <summary>Shown instead of a price when the warehouse is at the supported ceiling.</summary>
        public const string UpgradeMaxed = "ui.upgrade_maxed";

        /// <summary>{0} = formatted mm:ss.</summary>
        public const string RefreshIn = "ui.refresh_in";

        /// <summary>
        /// Every key this module resolves from code. <c>TradeTextTests</c> checks each one exists
        /// in every locale — add a new key here, or the test cannot see it.
        /// </summary>
        public static readonly string[] AllKeys =
        {
            BlockerInsufficient, WarningOverflow, BlockerUnavailable,
            WarehouseAmount, WarehouseLevel,
            UpgradeCostCrystals, UpgradeCostPaired, UpgradeMaxed,
            RefreshIn
        };
    }
}
