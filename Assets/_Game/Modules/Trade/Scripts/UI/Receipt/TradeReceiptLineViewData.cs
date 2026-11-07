using UnityEngine;

namespace KingdomRuler.Modules.Trade.UI
{
    /// <summary>One line of the receipt: "+13 Wood", or "17 Wood lost — the warehouse is full".</summary>
    public readonly struct TradeReceiptLineViewData
    {
        public readonly Sprite Icon;
        public readonly string Text;

        /// <summary>A loss to a full warehouse, drawn so it cannot be mistaken for a gain.</summary>
        public readonly bool IsLoss;

        public TradeReceiptLineViewData(Sprite icon, string text, bool isLoss)
        {
            Icon   = icon;
            Text   = text;
            IsLoss = isLoss;
        }
    }
}
