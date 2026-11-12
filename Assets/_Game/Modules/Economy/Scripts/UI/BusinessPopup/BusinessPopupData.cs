using System;
using UnityEngine;

namespace KingdomRuler.Modules.Economy.UI
{
    /// <summary>What one business's panel draws, already localized and abbreviated.</summary>
    /// <remarks>
    /// <see cref="IEquatable{T}"/> because the open panel re-reads it several times a second and
    /// redraws only on a change; the default struct comparison would box and reflect on every read.
    /// </remarks>
    public readonly struct BusinessPopupData : IEquatable<BusinessPopupData>
    {
        /// <summary>Null while art is outstanding; the panel hides the slot.</summary>
        public readonly Sprite Artwork;
        public readonly string Name;
        public readonly string Flavor;

        /// <summary>"12 owned".</summary>
        public readonly string OwnedText;

        /// <summary>"+720 per hour".</summary>
        public readonly string RateText;

        public readonly long   Stored;
        public readonly long   Capacity;
        public readonly string StoredText;
        public readonly string CapacityText;

        /// <summary>The storage holds gold: Collect is active.</summary>
        public readonly bool CanCollect;

        /// <summary>False for an option past the units ceiling: that button is inactive.</summary>
        public readonly bool CanBuyOne, CanBuyTen, CanBuyMax;

        /// <summary>The gold covers it. An option the player cannot afford stays tappable — it offers the crystal top-up — and is only drawn muted.</summary>
        public readonly bool AffordsOne, AffordsTen, AffordsMax;

        public readonly string OnePrice;
        public readonly string TenPrice;

        /// <summary>"Max ×7": how many Max buys, and its price.</summary>
        public readonly string MaxText;
        public readonly string MaxPrice;

        /// <summary>"Maximum owned" at the units ceiling, shown instead of the prices; otherwise null.</summary>
        public readonly string MaxedText;

        public BusinessPopupData(
            Sprite artwork, string name, string flavor, string ownedText, string rateText,
            long stored, long capacity, string storedText, string capacityText, bool canCollect,
            bool canBuyOne, bool canBuyTen, bool canBuyMax, bool affordsOne, bool affordsTen, bool affordsMax,
            string onePrice, string tenPrice, string maxText, string maxPrice, string maxedText)
        {
            Artwork      = artwork;
            Name         = name;
            Flavor       = flavor;
            OwnedText    = ownedText;
            RateText     = rateText;
            Stored       = stored;
            Capacity     = capacity;
            StoredText   = storedText;
            CapacityText = capacityText;
            CanCollect   = canCollect;
            CanBuyOne    = canBuyOne;
            CanBuyTen    = canBuyTen;
            CanBuyMax    = canBuyMax;
            AffordsOne   = affordsOne;
            AffordsTen   = affordsTen;
            AffordsMax   = affordsMax;
            OnePrice     = onePrice;
            TenPrice     = tenPrice;
            MaxText      = maxText;
            MaxPrice     = maxPrice;
            MaxedText    = maxedText;
        }

        /// <summary><see cref="Stored"/> / <see cref="Capacity"/>, 0..1.</summary>
        public float Fill => Capacity > 0 ? Mathf.Clamp01((float)((double)Stored / Capacity)) : 0f;

        public bool Equals(BusinessPopupData other) =>
            Artwork == other.Artwork && Name == other.Name && Flavor == other.Flavor
            && OwnedText == other.OwnedText && RateText == other.RateText
            && Stored == other.Stored && Capacity == other.Capacity
            && StoredText == other.StoredText && CapacityText == other.CapacityText
            && CanCollect == other.CanCollect
            && CanBuyOne == other.CanBuyOne && CanBuyTen == other.CanBuyTen && CanBuyMax == other.CanBuyMax
            && AffordsOne == other.AffordsOne && AffordsTen == other.AffordsTen && AffordsMax == other.AffordsMax
            && OnePrice == other.OnePrice && TenPrice == other.TenPrice
            && MaxText == other.MaxText && MaxPrice == other.MaxPrice && MaxedText == other.MaxedText;

        public override bool Equals(object obj) => obj is BusinessPopupData other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Name, Stored, Capacity, OwnedText, MaxText);
    }
}
