using System;
using System.Collections.Generic;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Trade
{
    /// <summary>
    /// Generates a batch of offers (GDD §7): 30% profitable, 45% neutral, 25% unprofitable as a
    /// long-run average; 2–3 resources given and 2–3 different ones received; sized by
    /// <see cref="TradeOfferSizing"/>.
    /// </summary>
    /// <remarks>
    /// The mix is a long-run average because no integer split of 10 reaches 45%: each batch takes
    /// the floor (3 / 4 / 2) and awards the two spare slots by weighted chance
    /// (<see cref="TradeOfferAllocator.ApportionStochastic"/>), so any one list is 3/5/2 or 3/4/3.
    /// </remarks>
    public sealed class TradeOfferGenerator
    {
        /// <summary>GDD §7's target mix, in <see cref="Bands"/> order.</summary>
        private static readonly double[] BandShares = { 0.30d, 0.45d, 0.25d };

        private static readonly TradeProfitability[] Bands =
            { TradeProfitability.Profitable, TradeProfitability.Neutral, TradeProfitability.Unprofitable };

        private static readonly TradeResourceType[] AllResources =
            (TradeResourceType[])Enum.GetValues(typeof(TradeResourceType));

        public const int MinTypesPerSide = 2;
        public const int MaxTypesPerSide = 3;

        private readonly Random _rng;

        public TradeOfferGenerator(Random rng)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        /// <summary>
        /// <paramref name="count"/> offers, shuffled so the bands are not in blocks, sized from
        /// <paramref name="capacities"/> as they are now.
        /// </summary>
        /// <exception cref="ArgumentException">A resource has no capacity.</exception>
        public List<TradeOffer> GenerateBatch(int count, float capacityShare,
                                              IReadOnlyDictionary<TradeResourceType, int> capacities)
        {
            if (capacities == null) throw new ArgumentNullException(nameof(capacities));
            foreach (var resource in AllResources)
                if (!capacities.ContainsKey(resource))
                    throw new ArgumentException($"No capacity given for {resource}.", nameof(capacities));

            var offers = new List<TradeOffer>(Math.Max(0, count));
            if (count <= 0) return offers;

            var targets = new double[BandShares.Length];
            for (int i = 0; i < targets.Length; i++) targets[i] = BandShares[i] * count;
            var perBand = TradeOfferAllocator.ApportionStochastic(targets, count, _rng);

            for (int band = 0; band < perBand.Length; band++)
                for (int i = 0; i < perBand[band]; i++)
                    offers.Add(GenerateOffer(Bands[band], capacityShare, capacities));

            Shuffle(offers);
            return offers;
        }

        private TradeOffer GenerateOffer(TradeProfitability band, float capacityShare,
                                         IReadOnlyDictionary<TradeResourceType, int> capacities)
        {
            // Both sides come from one shuffled list, so they can never share a resource. The counts
            // are clamped rather than assumed, so a different number of resources degrades instead
            // of producing a side with fewer types than asked for.
            var resources = new List<TradeResourceType>(AllResources);
            Shuffle(resources);

            int maxPerSide   = Math.Min(MaxTypesPerSide, resources.Count / 2);
            int minPerSide   = Math.Min(MinTypesPerSide, maxPerSide);
            int giveCount    = _rng.Next(minPerSide, maxPerSide + 1);
            int receiveCount = _rng.Next(minPerSide, maxPerSide + 1);

            var giveTypes    = resources.GetRange(0, giveCount);
            var receiveTypes = resources.GetRange(giveCount, receiveCount);
            var giveCaps     = CapacitiesOf(giveTypes, capacities);
            var receiveCaps  = CapacitiesOf(receiveTypes, capacities);

            int giveTotal    = TradeOfferSizing.GiveTotal(capacityShare, giveCaps, receiveCaps);
            int receiveTotal = TradeOfferSizing.ReceiveTotal(giveTotal, Multiplier(band));

            return new TradeOffer(
                Guid.NewGuid().ToString("N"),
                Amounts(giveTypes, TradeOfferSizing.Split(giveTotal, giveCaps, _rng)),
                Amounts(receiveTypes, TradeOfferSizing.Split(receiveTotal, receiveCaps, _rng)),
                band);
        }

        /// <summary>Units received per unit given. With every resource worth the same, this alone sets the deal.</summary>
        private double Multiplier(TradeProfitability band)
        {
            switch (band)
            {
                case TradeProfitability.Profitable:   return 1.10d + _rng.NextDouble() * 0.30d;   // 110–140%
                case TradeProfitability.Unprofitable: return 0.60d + _rng.NextDouble() * 0.30d;   //  60– 90%
                default:                              return 0.95d + _rng.NextDouble() * 0.10d;   //  95–105%
            }
        }

        private static int[] CapacitiesOf(List<TradeResourceType> types, IReadOnlyDictionary<TradeResourceType, int> capacities)
        {
            var result = new int[types.Count];
            for (int i = 0; i < result.Length; i++) result[i] = capacities[types[i]];
            return result;
        }

        private static TradeAmount[] Amounts(List<TradeResourceType> types, int[] units)
        {
            var result = new TradeAmount[types.Count];
            for (int i = 0; i < result.Length; i++) result[i] = new TradeAmount(types[i], units[i]);
            return result;
        }

        private void Shuffle<T>(List<T> list)
        {
            // Fisher–Yates.
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
