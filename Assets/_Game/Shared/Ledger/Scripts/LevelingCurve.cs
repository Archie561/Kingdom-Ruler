using System;

namespace KingdomRuler.Shared.Ledger
{
    /// <summary>
    /// The points-required-per-level curve, as a formula rather than a lookup table
    /// (GDD.md §6). Owned by <see cref="KingdomLedger"/> — callers never supply their own,
    /// which is what previously let Laws and the occurrences module level the same
    /// characteristic at different rates (ARCHITECTURE.md §4.3).
    ///
    /// Plain C# by design: keeping this out of ScriptableObject-land means the Ledger
    /// carries no UnityEngine types and the math is directly unit-testable.
    /// Designer-editable coefficients live on LevelingConfig, which produces one of these.
    /// </summary>
    public readonly struct LevelingCurve
    {
        public const float DefaultBasePoints     = 100f;
        public const float DefaultGrowthFactor   = 1.35f;
        public const float DefaultRoundToNearest = 10f;

        /// <summary>Points required to clear level 1.</summary>
        public readonly float BasePoints;

        /// <summary>Multiplier applied per level. &gt; 1 makes each level cost more than the last.</summary>
        public readonly float GrowthFactor;

        /// <summary>Result is rounded to the nearest multiple of this, for legible numbers.</summary>
        public readonly float RoundToNearest;

        public LevelingCurve(float basePoints, float growthFactor, float roundToNearest)
        {
            BasePoints     = basePoints;
            GrowthFactor   = growthFactor;
            RoundToNearest = roundToNearest;
        }

        /// <summary>The GDD §6 default curve: 100 × 1.35^(N-1), rounded to the nearest 10.</summary>
        public static LevelingCurve Default =>
            new LevelingCurve(DefaultBasePoints, DefaultGrowthFactor, DefaultRoundToNearest);

        /// <summary>
        /// False for a zeroed struct or nonsensical coefficients. Checked at Ledger
        /// construction so a misconfigured curve fails loudly at composition time
        /// rather than quietly mis-levelling every characteristic in the game.
        /// </summary>
        public bool IsValid => BasePoints > 0f && GrowthFactor > 0f && RoundToNearest > 0f;

        /// <summary>
        /// Points needed to advance from <paramref name="level"/> to the next one.
        /// </summary>
        /// <remarks>
        /// Guaranteed to return a strictly positive number. This is load-bearing:
        /// <see cref="KingdomLedger.AddCharacteristicPoints"/> loops while the accumulated
        /// points meet the requirement, so a zero would spin forever and publish level-up
        /// events without end. Rather than trusting every construction path, the invariant
        /// is enforced here — including for <c>default(LevelingCurve)</c>, which bypasses
        /// the constructor entirely and would otherwise have all-zero coefficients.
        /// </remarks>
        public float PointsRequired(int level)
        {
            if (level < 1) throw new ArgumentException("Level must be >= 1.", nameof(level));

            float basePoints = BasePoints     > 0f ? BasePoints     : DefaultBasePoints;
            float growth     = GrowthFactor   > 0f ? GrowthFactor   : DefaultGrowthFactor;
            float roundTo    = RoundToNearest > 0f ? RoundToNearest : DefaultRoundToNearest;

            double raw     = basePoints * Math.Pow(growth, level - 1);
            double rounded = Math.Round(raw / roundTo) * roundTo;

            // A shrinking curve can round down to zero at high levels; floor it.
            return (float)Math.Max(rounded, roundTo);
        }

        public override string ToString() =>
            $"LevelingCurve(base={BasePoints}, growth={GrowthFactor}, roundTo={RoundToNearest})";
    }
}
