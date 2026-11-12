namespace KingdomRuler.Modules.Economy.UI
{
    /// <summary>The three buy buttons in a business's panel (GDD §8).</summary>
    public enum BuyAmount
    {
        One,
        Ten,

        /// <summary>As many as the gold pays for — or one, offered with a crystal top-up, when none is.</summary>
        Max
    }
}
