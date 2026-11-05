using System;

namespace KingdomRuler.Systems.Ledger
{
    /// <summary>
    /// How many points a characteristic needs to clear each level (<c>GDD.md</c> §6):
    /// <c>required(N) = round(base × growth^(N-1) / roundTo) × roundTo</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Only <see cref="KingdomLedger"/> reads it.</b> Several mechanics award
    /// characteristic points, and a characteristic must level at the same rate whichever one did
    /// (<c>ARCHITECTURE.md</c> §4.3).</para>
    ///
    /// <para>Constants rather than an asset: they are rules of the Ledger, so retuning is a code
    /// change. A formula rather than a table keeps every level defined, including unreached ones.</para>
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
        /// meet the requirement. A test checks it across 200 levels.
        /// </remarks>
        public static float PointsRequired(int level)
        {
            if (level < 1) throw new ArgumentException("Level must be >= 1.", nameof(level));

            double raw = BasePoints * Math.Pow(GrowthFactor, level - 1);
            return (float)(Math.Round(raw / RoundToNearest) * RoundToNearest);
        }
    }
}
