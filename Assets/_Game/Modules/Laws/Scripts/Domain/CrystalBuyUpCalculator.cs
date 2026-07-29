using System;

namespace KingdomRuler.Modules.Laws.Domain
{
    /// <summary>
    /// Calculates crystal cost to instantly fill remaining points to next level.
    /// Formula: ceil(pointsRemaining / divisor), minimum 1.
    /// </summary>
    public static class CrystalBuyUpCalculator
    {
        /// <summary>
        /// Calculate crystal cost for buying up to the next characteristic level.
        /// </summary>
        /// <param name="pointsRemaining">Points still needed to reach next level.</param>
        /// <param name="divisor">Points per crystal (default 20 from GDD).</param>
        /// <returns>Crystal cost, minimum 1.</returns>
        public static int CalculateCost(float pointsRemaining, float divisor = 20f)
        {
            if (pointsRemaining <= 0f) return 1;
            if (divisor <= 0f) throw new ArgumentException("Divisor must be positive.", nameof(divisor));
            return Math.Max(1, (int)Math.Ceiling(pointsRemaining / divisor));
        }
    }
}
