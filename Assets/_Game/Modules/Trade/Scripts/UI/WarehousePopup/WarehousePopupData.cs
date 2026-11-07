using System;
using UnityEngine;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Trade.UI
{
    /// <summary>What one resource's warehouse popup draws, already localized.</summary>
    /// <remarks>
    /// <see cref="IEquatable{T}"/> because the popup re-reads it several times a second. The
    /// next-unit progress is a whole percent, so the popup redraws when the bar visibly moves rather
    /// than on every read.
    /// </remarks>
    public readonly struct WarehousePopupData : IEquatable<WarehousePopupData>
    {
        public readonly TradeResourceType Type;
        public readonly Sprite Icon;
        public readonly string Title;

        /// <summary>Counting from 1, and its text.</summary>
        public readonly int    DisplayedLevel;
        public readonly string LevelText;

        public readonly int Stock;
        public readonly int Capacity;

        /// <summary>False while full, and when a unit takes under a second — the bar would only flicker.</summary>
        public readonly bool ShowsNextUnit;

        /// <summary>Progress toward the next unit, 0..100.</summary>
        public readonly int NextUnitPercent;

        /// <summary>"+31 per hour".</summary>
        public readonly string RateText;

        /// <summary>"Full in 5:12:03", or "Full".</summary>
        public readonly string FullText;

        /// <summary>False at the level cap: both buttons inactive, <see cref="MaxedText"/> shown instead of the prices.</summary>
        public readonly bool   CanUpgrade;
        public readonly string CrystalPrice;
        public readonly string PairedPrice;
        public readonly string MaxedText;

        public WarehousePopupData(
            TradeResourceType type, Sprite icon, string title, int displayedLevel, string levelText,
            int stock, int capacity, bool showsNextUnit, int nextUnitPercent, string rateText, string fullText,
            bool canUpgrade, string crystalPrice, string pairedPrice, string maxedText)
        {
            Type            = type;
            Icon            = icon;
            Title           = title;
            DisplayedLevel  = displayedLevel;
            LevelText       = levelText;
            Stock           = stock;
            Capacity        = capacity;
            ShowsNextUnit   = showsNextUnit;
            NextUnitPercent = nextUnitPercent;
            RateText        = rateText;
            FullText        = fullText;
            CanUpgrade      = canUpgrade;
            CrystalPrice    = crystalPrice;
            PairedPrice     = pairedPrice;
            MaxedText       = maxedText;
        }

        public float Fill => Capacity > 0 ? Mathf.Clamp01((float)Stock / Capacity) : 0f;

        public bool Equals(WarehousePopupData other) =>
            Type == other.Type && Icon == other.Icon && Title == other.Title
            && DisplayedLevel == other.DisplayedLevel && LevelText == other.LevelText
            && Stock == other.Stock && Capacity == other.Capacity
            && ShowsNextUnit == other.ShowsNextUnit && NextUnitPercent == other.NextUnitPercent
            && RateText == other.RateText && FullText == other.FullText
            && CanUpgrade == other.CanUpgrade && CrystalPrice == other.CrystalPrice
            && PairedPrice == other.PairedPrice && MaxedText == other.MaxedText;

        public override bool Equals(object obj) => obj is WarehousePopupData other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Type, DisplayedLevel, Stock, Capacity, NextUnitPercent);
    }
}
