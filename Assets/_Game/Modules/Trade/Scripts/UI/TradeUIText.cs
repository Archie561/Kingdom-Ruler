namespace KingdomRuler.Modules.Trade.UI
{
    /// <summary>
    /// Trade text built in code with no data asset behind it. Public so <c>TradeTextTests</c>
    /// checks the String Table through these same constants. Fixed chrome is not here: it is a
    /// <c>LocalizeStringEvent</c> on the prefab. Resource names live in <c>SharedTable</c>.
    /// </summary>
    public static class TradeUIText
    {
        public const string StringTable = "TradeUITable";

        // ── The offer confirmation ────────────────────────────────────────────────

        /// <summary>{0} = shortfall, {1} = resource. The player cannot pay; Accept is disabled.</summary>
        public const string BlockerInsufficient = "ui.blocker_insufficient";

        /// <summary>{0} = units that will be lost, {1} = resource. A warning; Accept stays enabled.</summary>
        public const string OfferOverflow = "ui.offer.overflow";

        // ── The receipt after an accept ───────────────────────────────────────────

        /// <summary>{0} = units, {1} = resource.</summary>
        public const string ReceiptReceived = "ui.receipt.received";

        /// <summary>{0} = units, {1} = resource.</summary>
        public const string ReceiptForfeited = "ui.receipt.forfeited";

        // ── Warehouses: tiles and the warehouse popup ─────────────────────────────

        /// <summary>{0} = level, counting from 1.</summary>
        public const string WarehouseLevel = "ui.warehouse_level";

        /// <summary>{0} = resource.</summary>
        public const string WarehouseTitle = "ui.warehouse.title";

        /// <summary>{0} = whole units per hour.</summary>
        public const string WarehouseRate = "ui.warehouse.rate";

        /// <summary>{0} = h:mm:ss.</summary>
        public const string WarehouseFullIn = "ui.warehouse.full_in";

        public const string WarehouseFull = "ui.warehouse.full";

        /// <summary>{0} = crystals.</summary>
        public const string UpgradeCostCrystals = "ui.upgrade_cost_crystals";

        /// <summary>{0} = units, {1} = the paired resource.</summary>
        public const string UpgradeCostPaired = "ui.upgrade_cost_paired";

        /// <summary>Shown instead of the prices at the level cap.</summary>
        public const string UpgradeMaxed = "ui.upgrade_maxed";

        public const string UpgradeTitle = "ui.upgrade.title";

        /// <summary>{0} = crystals, {1} = resource, {2} = the level reached, {3} = the capacity then.</summary>
        public const string UpgradeBodyCrystals = "ui.upgrade.body_crystals";

        /// <summary>{0} = units, {1} = the paired resource, {2} = resource, {3} = the level reached, {4} = the capacity then.</summary>
        public const string UpgradeBodyPaired = "ui.upgrade.body_paired";

        /// <summary>{0} = the paired resource, {1} = how much the player holds.</summary>
        public const string NotEnoughPaired = "ui.upgrade.not_enough_paired";

        // ── Refresh ───────────────────────────────────────────────────────────────

        /// <summary>{0} = m:ss. The offer list's header.</summary>
        public const string RefreshIn = "ui.refresh_in";

        public const string RefreshTitle = "ui.refresh.title";

        /// <summary>{0} = crystals, {1} = m:ss until the free refresh.</summary>
        public const string RefreshBody = "ui.refresh.body";

        /// <summary>Added to a crystal confirmation the player cannot afford.</summary>
        public const string NotEnoughCrystals = "ui.not_enough_crystals";

        /// <summary>Every key above, for <c>TradeTextTests</c>. A new code-resolved key goes here too.</summary>
        public static readonly string[] AllKeys =
        {
            BlockerInsufficient, OfferOverflow, ReceiptReceived, ReceiptForfeited,
            WarehouseLevel, WarehouseTitle, WarehouseRate, WarehouseFullIn, WarehouseFull,
            UpgradeCostCrystals, UpgradeCostPaired, UpgradeMaxed,
            UpgradeTitle, UpgradeBodyCrystals, UpgradeBodyPaired, NotEnoughPaired,
            RefreshIn, RefreshTitle, RefreshBody, NotEnoughCrystals
        };
    }
}
