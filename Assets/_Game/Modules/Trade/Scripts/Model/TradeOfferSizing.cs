using System;
using System.Collections.Generic;

namespace KingdomRuler.Modules.Trade
{
    /// <summary>
    /// How big an offer is: a share of the warehouses it trades with, measured on the smaller side,
    /// split across each side's resources in proportion to their capacities (GDD §7).
    /// </summary>
    /// <remarks>
    /// <para>Measuring on the smaller side bounds every single amount by its <i>own</i> warehouse:
    /// a give amount is at most ≈ share × 5/3 of its warehouse, a receive amount ≈ share × 1.4 × 5/3.
    /// So no offer asks for more than a warehouse holds, and none overflows a warehouse that is not
    /// already about half full (at the default share). <c>TradeConfig.MaxOfferCapacityShare</c> is
    /// the largest share for which that still holds.</para>
    ///
    /// <para>Only the totals are scaled, and all resources are worth one unit each, so an offer's
    /// profitability is still exactly its receive total against its give total.</para>
    /// </remarks>
    public static class TradeOfferSizing
    {
        /// <summary>
        /// The smallest give total. It keeps the profitability bands apart at any capacity: from 10
        /// up, 110% always rounds above the give total and 90% always below it.
        /// </summary>
        public const int MinGiveTotal = 10;

        /// <summary>The random spread on each resource's share of a side, so offers are not all evenly split.</summary>
        public const double JitterMin = 0.75;
        public const double JitterMax = 1.25;

        /// <summary><c>max(10, round(share × min(Σ give capacities, Σ receive capacities)))</c>.</summary>
        public static int GiveTotal(float share, IReadOnlyList<int> giveCapacities, IReadOnlyList<int> receiveCapacities)
        {
            long smallerSide = Math.Min(Sum(giveCapacities), Sum(receiveCapacities));
            return Math.Max(MinGiveTotal, RoundHalfUp(share * (double)smallerSide));
        }

        /// <summary>What the player receives for <paramref name="giveTotal"/> at a profitability multiplier.</summary>
        public static int ReceiveTotal(int giveTotal, double multiplier) => Math.Max(1, RoundHalfUp(giveTotal * multiplier));

        /// <summary>
        /// Split <paramref name="total"/> across resources with these capacities, in proportion to
        /// capacity × a random jitter. Every resource gets at least 1, and the parts sum exactly to
        /// <paramref name="total"/> — or to the resource count, if the total is smaller.
        /// </summary>
        public static int[] Split(int total, IReadOnlyList<int> capacities, Random rng)
        {
            if (capacities == null) throw new ArgumentNullException(nameof(capacities));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            int count  = capacities.Count;
            var result = new int[count];
            if (count == 0) return result;

            var weights = new double[count];
            double weightSum = 0d;
            for (int i = 0; i < count; i++)
            {
                double jitter = JitterMin + rng.NextDouble() * (JitterMax - JitterMin);
                weights[i] = Math.Max(1, capacities[i]) * jitter;
                weightSum += weights[i];
            }

            // One unit per resource is reserved first, so no share can round down to nothing.
            int rest    = Math.Max(total, count) - count;
            var targets = new double[count];
            for (int i = 0; i < count; i++) targets[i] = weights[i] / weightSum * rest;

            var extra = TradeOfferAllocator.ApportionLargestRemainder(targets, rest);
            for (int i = 0; i < count; i++) result[i] = 1 + extra[i];
            return result;
        }

        /// <remarks>Explicit: <c>Math.Round</c> on its own rounds halves to even.</remarks>
        private static int RoundHalfUp(double value) =>
            (int)Math.Min(int.MaxValue, Math.Round(value, MidpointRounding.AwayFromZero));

        private static long Sum(IReadOnlyList<int> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            long sum = 0;
            foreach (int value in values) sum += value;
            return sum;
        }
    }
}
