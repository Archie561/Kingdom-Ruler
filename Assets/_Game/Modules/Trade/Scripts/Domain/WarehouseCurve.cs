using System;

namespace KingdomRuler.Modules.Trade.Domain
{
    /// <summary>
    /// A growth curve over warehouse upgrade levels, as a formula rather than a lookup table
    /// (<c>GDD.md</c> §7). Used twice with different coefficients: once for capacity at a level,
    /// once for the crystal price of the next upgrade.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a formula.</b> §7 asked for the "same style of curve as §6", and §6 rejects
    /// arrays outright: "a formula keeps every level defined, including ones no designer has
    /// reached yet." The arrays this replaces held six capacity entries and five cost entries,
    /// so past level five the old fallbacks diverged — capacity grew exponentially while cost
    /// grew linearly, which made a level-50 warehouse cheap. A formula has no edge to fall off.</para>
    ///
    /// <para><b>Module-owned, not Ledger-owned.</b> The §4.3 ownership test asks whether a second
    /// mechanic performing this operation would have to match, and only Trade upgrades
    /// warehouses. Trade computes a capacity and hands the Ledger the number; the Ledger derives
    /// the regen rate from it. Modules compute a number and hand it over — they never hand the
    /// Ledger a way to compute.</para>
    ///
    /// <para>Plain C# by design, mirroring <c>LevelingCurve</c>: no <c>UnityEngine</c> types, so
    /// it is directly unit-testable. Designer-editable coefficients live on <c>TradeConfig</c>,
    /// which produces one of these.</para>
    ///
    /// <para><b>The rounding is deliberately <c>Math.Round</c>'s banker's rounding</b>, matching
    /// <c>LevelingCurve.PointsRequired</c> exactly — so <c>100 × 1.5² = 225</c> yields
    /// <b>220</b>, not 230. Mirroring the sibling curve's expression matters more than the
    /// one-unit difference; a test pins it so nobody "corrects" it later. (This is the opposite
    /// call from the offer-ratio split, where the same ToEven behaviour silently stole a slot and
    /// had to go — see <see cref="TradeOfferAllocator"/>.)</para>
    /// </remarks>
    public readonly struct WarehouseCurve
    {
        public const float DefaultBaseValue     = 100f;
        public const float DefaultGrowthFactor  = 1.5f;
        public const float DefaultRoundToNearest = 10f;

        /// <summary>
        /// Highest level the curve will evaluate. A <b>save-integrity guard, not a design cap</b>:
        /// the save is plain JSON on the device, and a hand-edited <c>"Stone": 9999</c> would
        /// produce <c>100 × 1.5^9999</c> → <c>Infinity</c> → an infinite regen rate → <c>NaN</c>
        /// amounts, corrupting the economy irrecoverably (<c>CLAUDE.md</c> §1.2). 50 is far
        /// beyond any reachable play — capacity there is ~6.4 × 10¹⁰ — while staying comfortably
        /// inside float range.
        /// </summary>
        public const int MaxSupportedLevel = 50;

        /// <summary>Value at level 0 — the starting warehouse capacity, or the first upgrade's price.</summary>
        public readonly float BaseValue;

        /// <summary>Multiplier per level. &gt; 1 makes each level larger than the last.</summary>
        public readonly float GrowthFactor;

        /// <summary>Result is rounded to the nearest multiple of this, for legible numbers.</summary>
        public readonly float RoundToNearest;

        public WarehouseCurve(float baseValue, float growthFactor, float roundToNearest)
        {
            BaseValue      = baseValue;
            GrowthFactor   = growthFactor;
            RoundToNearest = roundToNearest;
        }

        /// <summary>The GDD §7 default capacity curve: 100 × 1.5^L, rounded to the nearest 10.</summary>
        public static WarehouseCurve DefaultCapacity =>
            new WarehouseCurve(DefaultBaseValue, DefaultGrowthFactor, DefaultRoundToNearest);

        /// <summary>The GDD §7 default crystal-price curve: 5 × 1.5^L, rounded to whole crystals.</summary>
        public static WarehouseCurve DefaultCrystalCost =>
            new WarehouseCurve(5f, DefaultGrowthFactor, 1f);

        /// <summary>
        /// False for a zeroed struct or nonsensical coefficients. Checked where the curve is
        /// built so a misconfigured asset fails loudly at composition time rather than quietly
        /// mis-sizing every warehouse in the game.
        /// </summary>
        public bool IsValid => BaseValue > 0f && GrowthFactor > 0f && RoundToNearest > 0f;

        /// <summary>
        /// The curve's value at <paramref name="level"/>, where level 0 is the base.
        /// </summary>
        /// <remarks>
        /// Guaranteed to return a finite, strictly positive number. Rather than trusting every
        /// construction path, the invariant is enforced here — including for
        /// <c>default(WarehouseCurve)</c>, which bypasses the constructor entirely and would
        /// otherwise have all-zero coefficients and return 0, giving a zero-capacity warehouse
        /// and a zero regen rate.
        /// </remarks>
        public float ValueAt(int level)
        {
            if (level < 0)
                throw new ArgumentException("Level must be >= 0.", nameof(level));

            // Clamp rather than throw: a corrupt save should degrade to the biggest sane
            // warehouse, not refuse to load. LoadFromDto clamps too, so this is belt-and-braces.
            int clamped = Math.Min(level, MaxSupportedLevel);

            float baseValue = BaseValue      > 0f ? BaseValue      : DefaultBaseValue;
            float growth    = GrowthFactor   > 0f ? GrowthFactor   : DefaultGrowthFactor;
            float roundTo   = RoundToNearest > 0f ? RoundToNearest : DefaultRoundToNearest;

            double raw     = baseValue * Math.Pow(growth, clamped);
            double rounded = Math.Round(raw / roundTo) * roundTo;

            // A shrinking curve can round down to zero at high levels; floor it so capacity is
            // never 0 (which would make the regen rate 0 and strand the warehouse empty).
            return (float)Math.Max(rounded, roundTo);
        }

        public override string ToString() =>
            $"WarehouseCurve(base={BaseValue}, growth={GrowthFactor}, roundTo={RoundToNearest})";
    }
}
