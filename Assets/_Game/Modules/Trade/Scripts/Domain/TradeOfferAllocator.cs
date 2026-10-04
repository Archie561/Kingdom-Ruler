using System;
using System.Collections.Generic;

namespace KingdomRuler.Modules.Trade.Domain
{
    /// <summary>
    /// Splits a whole number into parts matching real-valued targets, with the parts summing
    /// exactly to the total. Used twice: to divide a batch of offers into profitability bands,
    /// and to divide a resource total between 2–3 resource types.
    /// </summary>
    /// <remarks>
    /// <para><b>Two strategies, and picking the wrong one is the bug this class exists to
    /// fix.</b> The old generator used <c>Math.Round</c> per band, which is neither: at the GDD's
    /// batch size of 10, the 30/45/25 split is 3.0 / 4.5 / 2.5, and .NET's banker's rounding
    /// turns <c>Round(2.5)</c> into 2 — silently shipping 3/5/2, i.e. <b>30/50/20</b>, in the one
    /// case the GDD actually specifies.</para>
    ///
    /// <para><see cref="ApportionStochastic"/> is correct for the <b>ratio</b>: the two half-slots
    /// go to a randomly chosen band each batch, so the long-run average is exactly 30/45/25.
    /// Deterministic rounding cannot reach 45% of 10 — no integer does — so any fixed rule bakes
    /// in a permanent bias, which is precisely what <c>ARCHITECTURE.md</c> §8 means by testing the
    /// ratio "statistically, across many generated batches".</para>
    ///
    /// <para><see cref="ApportionLargestRemainder"/> is correct for <b>amounts</b>: a single offer
    /// should not be randomly lumpy, and nothing averages out across one offer. Deterministic is
    /// what you want when the result is looked at individually rather than in aggregate.</para>
    /// </remarks>
    public static class TradeOfferAllocator
    {
        /// <summary>
        /// Deterministic apportionment: floor everything, then hand the leftover units to the
        /// largest fractional parts. Parts sum exactly to <paramref name="total"/>.
        /// </summary>
        public static int[] ApportionLargestRemainder(IReadOnlyList<double> targets, int total)
        {
            var result = Floors(targets, total, out var fractions, out int remainder);
            if (remainder <= 0) return result;

            // Award the leftovers to the biggest fractions, largest first.
            var order = new int[targets.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) => fractions[b].CompareTo(fractions[a]));

            for (int i = 0; i < remainder; i++) result[order[i % order.Length]]++;
            return result;
        }

        /// <summary>
        /// Expectation-exact apportionment: floor everything, then award the leftover units so
        /// that each index's chance of receiving one equals its fractional part. Parts sum
        /// exactly to <paramref name="total"/>.
        /// </summary>
        /// <remarks>
        /// Systematic sampling, not independent draws per index: it guarantees the count is
        /// exactly right on every single batch while still giving each index its exact
        /// probability. Independent coin-flips would give the right average but a varying batch
        /// size, so some batches would hold 9 or 11 offers.
        /// </remarks>
        public static int[] ApportionStochastic(IReadOnlyList<double> targets, int total, Random rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            var result = Floors(targets, total, out var fractions, out int remainder);
            if (remainder <= 0) return result;

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

            // Floating-point residue can leave a unit unassigned. Give any shortfall to the
            // largest remaining fraction so the total is exact no matter what.
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

        private static int[] Floors(IReadOnlyList<double> targets, int total,
                                    out double[] fractions, out int remainder)
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
