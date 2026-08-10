using System;

namespace KingdomRuler.Modules.Laws.Domain
{
    /// <summary>
    /// Crystal cost to instantly finish the current characteristic level (GDD §6).
    /// </summary>
    /// <remarks>
    /// Laws-owned, not shared. Buy-up is offered only on the Laws screen, and it can never
    /// gain a second consumer: GDD §10 states Random Occurrences may affect "any resource
    /// except crystals — never the premium currency, to keep it monetization-safe".
    ///
    /// Contrast with the leveling curve, which *is* Ledger-owned: two mechanics award
    /// characteristic points, so that result must match across them. Nothing else computes
    /// a buy-up price. See ARCHITECTURE.md §4.3 for the placement test.
    /// </remarks>
    public static class CrystalBuyUpCalculator
    {
        /// <summary>
        /// Cost to finish the level: <c>ceil(remaining / divisor)</c>, minimum 1 —
        /// or 0 when there is nothing left to buy.
        /// </summary>
        /// <remarks>
        /// The minimum of 1 (GDD §6) exists so a tiny remainder still costs a crystal
        /// rather than rounding down to free. It is not meant to put a price on buying
        /// nothing: this used to clamp <paramref name="remaining"/> up to 0.01 and so
        /// quoted 1 crystal for an already-complete level, while LawsManager separately
        /// refused that same purchase as an invariant violation. Zero remaining now
        /// costs zero, and callers gate the button on that.
        /// </remarks>
        public static int CalculateCost(float remaining, float divisor)
        {
            if (divisor <= 0f)
                throw new ArgumentException("Divisor must be > 0.", nameof(divisor));

            if (remaining <= 0f) return 0;

            return Math.Max(1, (int)Math.Ceiling(remaining / divisor));
        }
    }
}
