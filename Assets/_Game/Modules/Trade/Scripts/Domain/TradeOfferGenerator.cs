using System;
using System.Collections.Generic;
using System.Linq;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Trade.Domain
{
    /// <summary>
    /// Generates a batch of trade offers matching <c>GDD.md</c> §7: 30% profitable, 45% neutral,
    /// 25% unprofitable, each giving 2–3 resource types and asking for 2–3 different ones.
    /// </summary>
    /// <remarks>
    /// <para><b>The ratio is a long-run average, not a per-batch guarantee</b>, because 45% of 10
    /// is 4.5 and no integer split reaches it. Each batch takes the floor (3 / 4 / 2) and awards
    /// the two leftover slots by weighted chance, so the expectation is exactly 30/45/25 — which
    /// is why <c>ARCHITECTURE.md</c> §8 asks for the ratio to be tested statistically across many
    /// batches rather than asserted on one. The previous implementation used <c>Math.Round</c>
    /// per band and, thanks to banker's rounding turning 2.5 into 2, shipped a permanent
    /// 30/50/20.</para>
    ///
    /// <para>Amounts are whole numbers, at least 1 per resource, summing exactly to the intended
    /// total. Fractional resource counts would be meaningless to a player and the old code could
    /// round a share down to zero, producing an offer that silently asked for nothing.</para>
    /// </remarks>
    public sealed class TradeOfferGenerator
    {
        /// <summary>GDD §7's target mix. Index order matches <see cref="BandOrder"/>.</summary>
        private static readonly double[] DefaultBandWeights = { 0.30d, 0.45d, 0.25d };

        private static readonly TradeProfitability[] BandOrder =
        {
            TradeProfitability.Profitable,
            TradeProfitability.Neutral,
            TradeProfitability.Unprofitable
        };

        private static readonly TradeResourceType[] AllResources =
            (TradeResourceType[])Enum.GetValues(typeof(TradeResourceType));

        /// <summary>Fewest resource types on either side of an offer (GDD §7).</summary>
        public const int MinTypesPerSide = 2;

        /// <summary>Most resource types on either side of an offer (GDD §7).</summary>
        public const int MaxTypesPerSide = 3;

        private readonly Random _rng;

        public TradeOfferGenerator(int? seed = null)
        {
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        /// <summary>Deterministic generator for tests, so a batch can be reproduced exactly.</summary>
        public TradeOfferGenerator(Random rng)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        /// <summary>
        /// Generate <paramref name="count"/> offers, shuffled so the profitability bands are not
        /// in blocks.
        /// </summary>
        public List<TradeOffer> GenerateBatch(int count, float baseAmount = 50f)
        {
            var offers = new List<TradeOffer>(Math.Max(0, count));
            if (count <= 0) return offers;

            var targets = DefaultBandWeights.Select(weight => weight * count).ToArray();
            var perBand = TradeOfferAllocator.ApportionStochastic(targets, count, _rng);

            for (int band = 0; band < perBand.Length; band++)
                for (int i = 0; i < perBand[band]; i++)
                    offers.Add(GenerateOffer(BandOrder[band], baseAmount));

            // Fisher-Yates, so the list isn't grouped by band.
            for (int i = offers.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (offers[i], offers[j]) = (offers[j], offers[i]);
            }

            return offers;
        }

        private TradeOffer GenerateOffer(TradeProfitability profitability, float baseAmount)
        {
            // Give and receive sets never overlap (GDD §7): both are taken from one shuffled
            // list. With 6 resources and at most 3 a side the two slices always fit, but the
            // counts are clamped rather than assumed so adding or removing a resource type can
            // never silently produce a side with fewer types than asked for.
            var shuffled = AllResources.OrderBy(_ => _rng.Next()).ToList();

            int maxPerSide   = Math.Min(MaxTypesPerSide, shuffled.Count / 2);
            int minPerSide   = Math.Min(MinTypesPerSide, maxPerSide);
            int giveCount    = _rng.Next(minPerSide, maxPerSide + 1);
            int receiveCount = _rng.Next(minPerSide, maxPerSide + 1);

            var giveTypes    = shuffled.Take(giveCount).ToList();
            var receiveTypes = shuffled.Skip(giveCount).Take(receiveCount).ToList();

            float totalGive    = Math.Max(baseAmount, giveTypes.Count);
            float totalReceive = totalGive * ReceiveMultiplier(profitability);

            return new TradeOffer(
                Guid.NewGuid().ToString("N"),
                DistributeAmong(giveTypes, totalGive),
                DistributeAmong(receiveTypes, totalReceive),
                profitability);
        }

        /// <summary>
        /// How much the player gets back per unit given. All six resources are valued identically
        /// (GDD §7), so this ratio alone decides whether an offer is a good deal.
        /// </summary>
        private float ReceiveMultiplier(TradeProfitability profitability)
        {
            switch (profitability)
            {
                case TradeProfitability.Profitable:   return 1.10f + (float)_rng.NextDouble() * 0.30f; // 110–140%
                case TradeProfitability.Unprofitable: return 0.60f + (float)_rng.NextDouble() * 0.30f; //  60– 90%
                default:                              return 0.95f + (float)_rng.NextDouble() * 0.10f; //  95–105%
            }
        }

        /// <summary>
        /// Split <paramref name="totalAmount"/> across <paramref name="types"/> as whole numbers
        /// that sum exactly to the rounded total, with every type receiving at least 1.
        /// </summary>
        /// <remarks>
        /// One unit per type is reserved up front and the rest apportioned by random weights, so
        /// a share can never round to zero — the old implementation could, leaving an offer that
        /// listed a resource but asked for none of it. Largest-remainder rather than stochastic
        /// apportionment here: a single offer is looked at individually, and nothing averages out
        /// across one of them.
        /// </remarks>
        private Dictionary<TradeResourceType, float> DistributeAmong(
            List<TradeResourceType> types, float totalAmount)
        {
            var result = new Dictionary<TradeResourceType, float>(types.Count);
            if (types.Count == 0) return result;

            int total     = Math.Max(types.Count, (int)Math.Round(totalAmount));
            int remaining = total - types.Count;      // one unit reserved per type

            var weights = new double[types.Count];
            for (int i = 0; i < weights.Length; i++) weights[i] = _rng.NextDouble() + 0.01d;

            double weightSum = weights.Sum();
            var targets = weights.Select(w => w / weightSum * remaining).ToArray();
            var extra   = TradeOfferAllocator.ApportionLargestRemainder(targets, remaining);

            for (int i = 0; i < types.Count; i++)
                result[types[i]] = 1f + extra[i];

            return result;
        }
    }
}
