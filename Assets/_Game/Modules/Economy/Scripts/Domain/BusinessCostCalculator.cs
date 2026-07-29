using System;

namespace KingdomRuler.Modules.Economy.Domain
{
    public static class BusinessCostCalculator
    {
        /// <summary>Cost of the (n+1)th unit: BaseCost * 1.15^n</summary>
        public static long CalculateCost(long baseCost, int countOwned, float costMultiplier = 1.15f)
        {
            if (baseCost < 0) throw new ArgumentException("Base cost must be non-negative.", nameof(baseCost));
            if (countOwned < 0) throw new ArgumentException("Count owned must be non-negative.", nameof(countOwned));
            return (long)Math.Ceiling(baseCost * Math.Pow(costMultiplier, countOwned));
        }
    }
}
