using System;
using UnityEngine;

namespace KingdomRuler.Modules.Trade.UI
{
    /// <summary>One "12 Stone" line: an icon, a whole amount and the resource's localized name.</summary>
    /// <remarks>The amount and the name are separate labels on the prefab, so word order is layout, not string building.</remarks>
    public readonly struct ResourceAmountViewData : IEquatable<ResourceAmountViewData>
    {
        /// <summary>Null while art is outstanding; the view hides the slot.</summary>
        public readonly Sprite Icon;

        public readonly int    Units;
        public readonly string Name;

        public ResourceAmountViewData(Sprite icon, int units, string name)
        {
            Icon  = icon;
            Units = units;
            Name  = name;
        }

        public bool Equals(ResourceAmountViewData other) => Icon == other.Icon && Units == other.Units && Name == other.Name;

        public override bool Equals(object obj) => obj is ResourceAmountViewData other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Units, Name);

        /// <summary>Element-by-element comparison, for the view-data structs that hold these in arrays.</summary>
        public static bool SequenceEqual(ResourceAmountViewData[] a, ResourceAmountViewData[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (!a[i].Equals(b[i])) return false;
            return true;
        }
    }
}
