using System;
using UnityEngine;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Laws.UI
{
    /// <summary>What <see cref="CharacteristicRowView"/> draws for one characteristic, already localized.</summary>
    /// <remarks>
    /// <see cref="IEquatable{T}"/> because the popup compares its data several times a second; the
    /// default struct comparison would box and reflect on every check.
    /// </remarks>
    public readonly struct CharacteristicRowViewData : IEquatable<CharacteristicRowViewData>
    {
        public readonly CharacteristicType Type;

        /// <summary>Null while art is outstanding; the row hides the slot.</summary>
        public readonly Sprite Icon;

        public readonly string Name;
        public readonly string LevelText;
        public readonly int    Level;

        /// <summary>Points into the current level, and the points that level needs.</summary>
        public readonly int Points;
        public readonly int PointsRequired;

        /// <summary><see cref="Points"/> / <see cref="PointsRequired"/>, 0..1.</summary>
        public readonly float Progress;

        /// <summary>Crystals to finish the level.</summary>
        public readonly int BuyUpCost;

        public CharacteristicRowViewData(
            CharacteristicType type, Sprite icon, string name, string levelText, int level,
            int points, int pointsRequired, float progress, int buyUpCost)
        {
            Type           = type;
            Icon           = icon;
            Name           = name;
            LevelText      = levelText;
            Level          = level;
            Points         = points;
            PointsRequired = pointsRequired;
            Progress       = progress;
            BuyUpCost      = buyUpCost;
        }

        public bool Equals(CharacteristicRowViewData other) =>
            Type == other.Type && Icon == other.Icon && Name == other.Name && LevelText == other.LevelText
            && Level == other.Level && Points == other.Points && PointsRequired == other.PointsRequired
            && BuyUpCost == other.BuyUpCost;

        public override bool Equals(object obj) => obj is CharacteristicRowViewData other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Type, Level, Points, BuyUpCost);
    }
}
