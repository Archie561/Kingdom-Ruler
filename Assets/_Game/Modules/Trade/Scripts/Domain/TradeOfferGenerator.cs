using System;
using System.Collections.Generic;
using System.Linq;
using KingdomRuler.Shared.Ledger;

namespace KingdomRuler.Modules.Trade.Domain
{
    /// <summary>
    /// Generates trade offers with the correct profitability distribution.
    /// 30% profitable, 45% neutral, 25% unprofitable.
    /// </summary>
    public sealed class TradeOfferGenerator
    {
        private readonly Random _rng;
        private static readonly TradeResourceType[] AllResources =
            (TradeResourceType[])Enum.GetValues(typeof(TradeResourceType));

        public TradeOfferGenerator(int? seed = null)
        {
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        /// <summary>
        /// Generate a batch of trade offers with the correct profitability distribution.
        /// </summary>
        public List<TradeOffer> GenerateBatch(int count, float baseAmount = 50f)
        {
            var offers = new List<TradeOffer>(count);
            int profitableCount = (int)Math.Round(count * 0.3);
            int unprofitableCount = (int)Math.Round(count * 0.25);
            int neutralCount = count - profitableCount - unprofitableCount;

            for (int i = 0; i < count; i++)
            {
                TradeProfitability profitability;
                if (i < profitableCount)
                    profitability = TradeProfitability.Profitable;
                else if (i < profitableCount + neutralCount)
                    profitability = TradeProfitability.Neutral;
                else
                    profitability = TradeProfitability.Unprofitable;

                offers.Add(GenerateOffer(profitability, baseAmount));
            }

            // Shuffle to randomize order
            for (int i = offers.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (offers[i], offers[j]) = (offers[j], offers[i]);
            }

            return offers;
        }

        private TradeOffer GenerateOffer(TradeProfitability profitability, float baseAmount)
        {
            // Pick 2-3 give and 2-3 receive resources, no overlap
            int giveCount = _rng.Next(2, 4); // 2 or 3
            int receiveCount = _rng.Next(2, 4);

            var shuffled = AllResources.OrderBy(_ => _rng.Next()).ToList();
            var giveTypes = shuffled.Take(giveCount).ToList();
            var receiveTypes = shuffled.Skip(giveCount).Take(receiveCount).ToList();

            // Calculate total amounts based on profitability
            float totalGive = baseAmount;
            float totalReceive;

            switch (profitability)
            {
                case TradeProfitability.Profitable:
                    totalReceive = totalGive * (1.1f + (float)_rng.NextDouble() * 0.3f); // 110-140%
                    break;
                case TradeProfitability.Unprofitable:
                    totalReceive = totalGive * (0.6f + (float)_rng.NextDouble() * 0.3f); // 60-90%
                    break;
                default: // Neutral
                    totalReceive = totalGive * (0.95f + (float)_rng.NextDouble() * 0.1f); // 95-105%
                    break;
            }

            var give = DistributeAmong(giveTypes, totalGive);
            var receive = DistributeAmong(receiveTypes, totalReceive);

            return new TradeOffer(
                Guid.NewGuid().ToString("N"),
                give, receive, profitability);
        }

        private Dictionary<TradeResourceType, float> DistributeAmong(
            List<TradeResourceType> types, float totalAmount)
        {
            var result = new Dictionary<TradeResourceType, float>();
            float remaining = totalAmount;

            for (int i = 0; i < types.Count - 1; i++)
            {
                // Give each type a random share (min 10% of total per slot)
                float minShare = totalAmount * 0.1f;
                float maxShare = remaining - minShare * (types.Count - 1 - i);
                float share = minShare + (float)_rng.NextDouble() * (maxShare - minShare);
                share = (float)Math.Round(share);
                result[types[i]] = share;
                remaining -= share;
            }

            result[types[types.Count - 1]] = (float)Math.Round(remaining);
            return result;
        }
    }
}
