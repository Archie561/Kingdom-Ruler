using System;
using System.Collections.Generic;

namespace KingdomRuler.Modules.Trade
{
    /// <summary>
    /// Splits a whole number into whole parts that follow real-valued targets and sum exactly to
    /// the total. Used to divide a batch into profitability bands and an offer's total between its
    /// resources.
    /// </summary>
    /// <remarks>
    /// <para>Two strategies, because the two uses need different things.
    /// <see cref="ApportionStochastic"/> is for a <b>ratio across many batches</b>: no integer
    /// split of 10 reaches 45%, so any fixed rounding rule bakes in a permanent bias, while
    /// awarding the fractional parts by chance makes the long-run average exact.
    /// <see cref="ApportionLargestRemainder"/> is for <b>one offer's amounts</b>: an offer is looked
    /// at on its own, nothing averages out across it, so it should not be randomly lumpy.</para>
    ///
    /// <para>Never <c>Math.Round</c> per part: it neither sums to the total nor keeps the ratio,
    /// and its banker's rounding turns the 2.5 unprofitable slots of a 10-offer batch into 2.</para>
    /// </remarks>
    public static class TradeOfferAllocator
    {
        /// <summary>
        /// Floor every target, then give the leftover units to the largest fractional parts.
        /// Deterministic; the parts sum exactly to <paramref name="total"/>.
        /// </summary>
        public static int[] ApportionLargestRemainder(IReadOnlyList<double> targets, int total)
        {
            var result = Floors(targets, total, out var fractions, out int remainder);
            if (remainder <= 0 || result.Length == 0) return result;

            var order = new int[result.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) => fractions[b].CompareTo(fractions[a]));

            for (int i = 0; i < remainder; i++) result[order[i % order.Length]]++;
            return result;
        }

        /// <summary>
        /// Floor every target, then award the leftover units so that each index's chance of
        /// receiving one equals its fractional part. The parts sum exactly to
        /// <paramref name="total"/> on every call, and each part's expectation is its target.
        /// </summary>
        /// <remarks>
        /// Systematic sampling — one random offset, then evenly spaced picks — rather than an
        /// independent coin flip per index, which would get the average right but let a batch of 10
        /// come out as 9 or 11.
        /// </remarks>
        public static int[] ApportionStochastic(IReadOnlyList<double> targets, int total, Random rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            var result = Floors(targets, total, out var fractions, out int remainder);
            if (remainder <= 0 || result.Length == 0) return result;

            double offset     = rng.NextDouble();
            double cumulative = 0d;
            int    picked     = 0;

            for (int i = 0; i < fractions.Length && picked < remainder; i++)
            {
                cumulative += fractions[i];
                while (picked < remainder && cumulative > offset + picked)
                {
                    result[i]++;
                    picked++;
                }
            }

            // Floating-point residue can leave a unit unawarded; the largest unused fraction gets it.
            while (picked < remainder)
            {
                int best = 0;
                for (int i = 1; i < fractions.Length; i++)
                    if (fractions[i] > fractions[best]) best = i;

                result[best]++;
                fractions[best] = -1d;
                picked++;
            }

            return result;
        }

        private static int[] Floors(IReadOnlyList<double> targets, int total, out double[] fractions, out int remainder)
        {
            if (targets == null) throw new ArgumentNullException(nameof(targets));

            var result = new int[targets.Count];
            fractions  = new double[targets.Count];

            int assigned = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                double target = targets[i] > 0d ? targets[i] : 0d;
                int    floor  = (int)Math.Floor(target);

                result[i]    = floor;
                fractions[i] = target - floor;
                assigned    += floor;
            }

            remainder = total - assigned;
            return result;
        }
    }
}
