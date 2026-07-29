using System;

namespace KingdomRuler.Modules.Laws.Domain
{
    /// <summary>
    /// Pure leveling math. The curve is injected as an array from LawsConfig
    /// so designers can hand-tune per characteristic later.
    /// </summary>
    public static class LevelingMath
    {
        /// <summary>
        /// Default formula: 100 × 1.35^(level-1), rounded to nearest 10.
        /// Used to generate the initial curve array; designers can override individual entries.
        /// </summary>
        public static float DefaultPointsRequired(int level)
        {
            if (level < 1) throw new ArgumentException("Level must be >= 1.", nameof(level));
            double raw = 100.0 * Math.Pow(1.35, level - 1);
            return (float)(Math.Round(raw / 10.0) * 10.0);
        }

        /// <summary>
        /// Look up points required from a designer-editable curve array.
        /// Index 0 = level 1. Falls back to formula for levels beyond the array.
        /// </summary>
        public static float PointsRequiredFromCurve(int level, float[] curve)
        {
            if (level < 1) throw new ArgumentException("Level must be >= 1.", nameof(level));
            int index = level - 1;
            if (curve != null && index < curve.Length)
                return curve[index];
            return DefaultPointsRequired(level);
        }
    }
}
