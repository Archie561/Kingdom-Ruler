using System.Collections.Generic;
using UnityEngine;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Trade.Presenters
{
    /// <summary>
    /// One "12 Stone" line on an offer row or in the confirmation panel.
    /// </summary>
    /// <remarks>
    /// <see cref="Name"/> arrives already localized and <see cref="Icon"/> already looked up, so
    /// the View never touches <c>TradeResourceRegistry</c> or <c>ILocalizationService</c>. The
    /// sprite is the one <c>UnityEngine.Object</c> allowed on a display struct, exactly as
    /// <c>CharacteristicDisplayData</c> carries one — the Presenter decides *what*, the View
    /// decides *how*.
    /// </remarks>
    public readonly struct TradeResourceLineData
    {
        public readonly TradeResourceType Type;
        public readonly string Name;
        public readonly Sprite Icon;
        public readonly float  Amount;
        public readonly string AmountText;

        public TradeResourceLineData(TradeResourceType type, string name, Sprite icon,
                                     float amount, string amountText)
        {
            Type       = type;
            Name       = name;
            Icon       = icon;
            Amount     = amount;
            AmountText = amountText;
        }
    }

    /// <summary>One row in the offer list.</summary>
    public readonly struct TradeOfferDisplayData
    {
        public readonly string OfferId;
        public readonly IReadOnlyList<TradeResourceLineData> Give;
        public readonly IReadOnlyList<TradeResourceLineData> Receive;
        public readonly float TotalGive;
        public readonly float TotalReceive;

        /// <summary>
        /// Whether the trade would go through right now.
        /// </summary>
        /// <remarks>
        /// <b>A styling hint only — never a reason to disable the row.</b> GDD §7 is explicit
        /// that an offer is never silently disabled; the row stays tappable and the confirmation
        /// panel explains what is wrong. Note this stays <c>true</c> when the only problem is a
        /// full warehouse, because overflow warns rather than blocks.
        /// </remarks>
        public readonly bool CanAccept;

        /// <summary>True when accepting would lose units to a full warehouse.</summary>
        public readonly bool HasOverflowWarning;

        public TradeOfferDisplayData(string offerId,
                                     IReadOnlyList<TradeResourceLineData> give,
                                     IReadOnlyList<TradeResourceLineData> receive,
                                     float totalGive, float totalReceive,
                                     bool canAccept, bool hasOverflowWarning)
        {
            OfferId            = offerId;
            Give               = give;
            Receive            = receive;
            TotalGive          = totalGive;
            TotalReceive       = totalReceive;
            CanAccept          = canAccept;
            HasOverflowWarning = hasOverflowWarning;
        }
    }

    /// <summary>The confirmation panel's contents, including the inline messages from GDD §7.</summary>
    public readonly struct TradeConfirmDisplayData
    {
        public readonly string OfferId;
        public readonly IReadOnlyList<TradeResourceLineData> Give;
        public readonly IReadOnlyList<TradeResourceLineData> Receive;

        /// <summary>Whether the Confirm button is enabled. True even with an overflow warning.</summary>
        public readonly bool CanAccept;

        /// <summary>Localized reasons the trade is refused. Empty when it can proceed.</summary>
        public readonly IReadOnlyList<string> BlockerMessages;

        /// <summary>
        /// Localized warnings the player may accept anyway — "your Stone warehouse only has room
        /// for 12". Shown alongside an enabled Confirm button.
        /// </summary>
        public readonly IReadOnlyList<string> WarningMessages;

        public TradeConfirmDisplayData(string offerId,
                                       IReadOnlyList<TradeResourceLineData> give,
                                       IReadOnlyList<TradeResourceLineData> receive,
                                       bool canAccept,
                                       IReadOnlyList<string> blockerMessages,
                                       IReadOnlyList<string> warningMessages)
        {
            OfferId         = offerId;
            Give            = give;
            Receive         = receive;
            CanAccept       = canAccept;
            BlockerMessages = blockerMessages;
            WarningMessages = warningMessages;
        }
    }

    /// <summary>One warehouse tile: its fill, its level, and both upgrade prices.</summary>
    public readonly struct WarehouseDisplayData
    {
        public readonly TradeResourceType Type;
        public readonly string Name;
        public readonly Sprite Icon;

        public readonly float  Amount;
        public readonly float  Capacity;
        /// <summary>Clamped to [0,1] so a View can bind it straight to a fill without guarding.</summary>
        public readonly float  FillFraction;
        public readonly string AmountText;

        public readonly int    Level;
        public readonly bool   CanUpgradeFurther;

        public readonly int    CrystalUpgradeCost;
        public readonly bool   CanAffordCrystalUpgrade;
        public readonly string CrystalCostText;

        public readonly TradeResourceType PairedType;
        public readonly string PairedName;
        public readonly float  PairedUpgradeCost;
        public readonly bool   CanAffordPairedUpgrade;
        public readonly string PairedCostText;

        public WarehouseDisplayData(
            TradeResourceType type, string name, Sprite icon,
            float amount, float capacity, float fillFraction, string amountText,
            int level, bool canUpgradeFurther,
            int crystalUpgradeCost, bool canAffordCrystalUpgrade, string crystalCostText,
            TradeResourceType pairedType, string pairedName,
            float pairedUpgradeCost, bool canAffordPairedUpgrade, string pairedCostText)
        {
            Type = type; Name = name; Icon = icon;
            Amount = amount; Capacity = capacity; FillFraction = fillFraction; AmountText = amountText;
            Level = level; CanUpgradeFurther = canUpgradeFurther;
            CrystalUpgradeCost = crystalUpgradeCost;
            CanAffordCrystalUpgrade = canAffordCrystalUpgrade;
            CrystalCostText = crystalCostText;
            PairedType = pairedType; PairedName = pairedName;
            PairedUpgradeCost = pairedUpgradeCost;
            CanAffordPairedUpgrade = canAffordPairedUpgrade;
            PairedCostText = pairedCostText;
        }
    }
}
