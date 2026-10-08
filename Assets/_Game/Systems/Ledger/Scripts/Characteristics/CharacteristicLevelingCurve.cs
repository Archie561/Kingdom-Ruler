using System;

namespace KingdomRuler.Systems.Ledger
{
    /// <summary>
    /// How many points a characteristic needs to clear each level (<c>GDD.md</c> §6):
    /// <c>required(N) = round(base × growth^(N-1) / roundTo) × roundTo</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Owned by the Ledger, and only the Ledger reads it.</b> Laws and Random Occurrences
    /// both award characteristic points, and a characteristic must level at the same rate whichever
    /// of them awarded the points. When each mechanic supplied its own curve, Laws built one from
    /// its config while the occurrences module used a flat <c>level => 100</c>, and the same
    /// characteristic levelled at two rates (<c>ARCHITECTURE.md</c> §4.3, <c>CLAUDE.md</c> §1.1).
    /// One formula, called only by <see cref="KingdomLedger"/>, makes that impossible.</para>
    ///
    /// <para><b>Constants, not an asset.</b> These are rules of the Ledger, so they live in its
    /// code; retuning the curve is a code change. A formula rather than a per-level table keeps
    /// every level defined, including ones no designer has reached yet.</para>
    /// </remarks>
    public static class CharacteristicLevelingCurve
    {
        /// <summary>Points required to clear level 1.</summary>
        public const float BasePoints = 100f;

        /// <summary>Multiplier per level. Above 1 means each level costs more than the last.</summary>
        public const float GrowthFactor = 1.35f;

        /// <summary>The result is rounded to the nearest multiple of this, for legible numbers.</summary>
        public const float RoundToNearest = 10f;

        /// <summary>Points needed to advance from <paramref name="level"/> to the next one.</summary>
        /// <remarks>
        /// Always strictly positive — load-bearing, because
        /// <see cref="KingdomLedger.AddCharacteristicPoints"/> levels up in a loop while the points
        /// meet the requirement. With fixed positive constants that holds by construction; a test
        /// checks it across 200 levels so an edit to the constants cannot break it unnoticed.
        /// </remarks>
        public static float PointsRequired(int level)
        {
            if (level < 1) throw new ArgumentException("Level must be >= 1.", nameof(level));

            double raw = BasePoints * Math.Pow(GrowthFactor, level - 1);
            return (float)(Math.Round(raw / RoundToNearest) * RoundToNearest);
        }
    }
}
