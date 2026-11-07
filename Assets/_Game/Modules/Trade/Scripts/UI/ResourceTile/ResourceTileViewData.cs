using UnityEngine;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Trade.UI
{
    /// <summary>The numbers a resource tile draws. Read every frame, so it carries no strings and allocates nothing.</summary>
    /// <remarks>The tile's text — name and "Level N" — is asked for separately, only when it can have changed.</remarks>
    public readonly struct ResourceTileViewData
    {
        public readonly TradeResourceType Type;

        /// <summary>Null while art is outstanding; the tile hides the slot.</summary>
        public readonly Sprite Icon;

        /// <summary>Counting from 1: a fresh warehouse is level 1.</summary>
        public readonly int DisplayedLevel;

        public readonly int Stock;
        public readonly int Capacity;

        public ResourceTileViewData(TradeResourceType type, Sprite icon, int displayedLevel, int stock, int capacity)
        {
            Type           = type;
            Icon           = icon;
            DisplayedLevel = displayedLevel;
            Stock          = stock;
            Capacity       = capacity;
        }

        /// <summary><see cref="Stock"/> / <see cref="Capacity"/>, 0..1.</summary>
        public float Fill => Capacity > 0 ? Mathf.Clamp01((float)Stock / Capacity) : 0f;
    }
}
