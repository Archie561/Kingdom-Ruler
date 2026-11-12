using UnityEngine;

namespace KingdomRuler.Modules.Economy.UI
{
    /// <summary>The numbers one business row draws. Read every frame, so it carries no strings and allocates nothing.</summary>
    /// <remarks>
    /// The row's text — name, units owned, the storage amounts — is asked for separately, only when it
    /// can have changed: the name on <c>StateChanged</c>, the numbers when their displayed value moves
    /// (<c>NumberFormat.DisplayedValue</c>).
    /// </remarks>
    public readonly struct BusinessRowViewData
    {
        /// <summary>Null while art is outstanding; the row hides the slot.</summary>
        public readonly Sprite Artwork;

        public readonly int  UnitsOwned;
        public readonly long Stored;
        public readonly long Capacity;
        public readonly bool IsFull;

        public BusinessRowViewData(Sprite artwork, int unitsOwned, long stored, long capacity, bool isFull)
        {
            Artwork    = artwork;
            UnitsOwned = unitsOwned;
            Stored     = stored;
            Capacity   = capacity;
            IsFull     = isFull;
        }

        /// <summary>The row's Collect button shows while the storage holds gold.</summary>
        public bool CanCollect => Stored > 0;

        /// <summary><see cref="Stored"/> / <see cref="Capacity"/>, 0..1.</summary>
        public float Fill => Capacity > 0 ? Mathf.Clamp01((float)((double)Stored / Capacity)) : 0f;
    }
}
