using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using KingdomRuler.Modules.Trade;
using KingdomRuler.Systems.Ledger;
using Random = System.Random;

namespace KingdomRuler.Tests.EditMode.Modules.Trade
{
    [TestFixture]
    public sealed class TradeOfferGeneratorTests
    {
        private const float Share = 0.20f;

        private static readonly TradeResourceType[] AllResources =
            (TradeResourceType[])Enum.GetValues(typeof(TradeResourceType));

        private TradeConfig _config;

        [SetUp]
        public void SetUp() => _config = ScriptableObject.CreateInstance<TradeConfig>();

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(_config);

        private static TradeOfferGenerator Seeded(int seed = 12345) => new TradeOfferGenerator(new Random(seed));

        private Dictionary<TradeResourceType, int> AllAtLevel(int level) =>
            AllResources.ToDictionary(r => r, r => WarehouseCurve.Capacity(_config, level));

        private Dictionary<TradeResourceType, int> RandomLevels(Random rng) =>
            AllResources.ToDictionary(r => r, r => WarehouseCurve.Capacity(_config, rng.Next(0, WarehouseCurve.TopLevel(_config) + 1)));

        private List<TradeOffer> Batch(TradeOfferGenerator generator, int count) =>
            generator.GenerateBatch(count, Share, AllAtLevel(0));

        // ── Batch shape ───────────────────────────────────────────────────────────

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(5)]
        [TestCase(10)]
        [TestCase(100)]
        public void GenerateBatch_ReturnsExactlyTheRequestedCount(int count)
        {
            Assert.AreEqual(count, Batch(Seeded(), count).Count);
        }

        [Test]
        public void GenerateBatch_WithZeroOrNegative_ReturnsEmpty()
        {
            Assert.IsEmpty(Batch(Seeded(), 0));
            Assert.IsEmpty(Batch(Seeded(), -5));
        }

        [Test]
        public void GenerateBatch_WithAResourceMissingItsCapacity_Throws()
        {
            var capacities = AllAtLevel(0);
            capacities.Remove(TradeResourceType.Clay);

            Assert.Throws<ArgumentException>(() => Seeded().GenerateBatch(10, Share, capacities));
        }

        [Test]
        public void SameSeed_ProducesTheSameBatch()
        {
            var first  = Batch(Seeded(777), 10);
            var second = Batch(Seeded(777), 10);

            CollectionAssert.AreEqual(first.Select(o => o.Profitability), second.Select(o => o.Profitability));
            CollectionAssert.AreEqual(first.Select(o => o.TotalGive), second.Select(o => o.TotalGive));
            CollectionAssert.AreEqual(first.Select(o => o.TotalReceive), second.Select(o => o.TotalReceive));
        }

        // ── The 30/45/25 ratio ────────────────────────────────────────────────────

        [Test]
        public void Distribution_Matches_30_45_25_AcrossManyBatches()
        {
            // No integer split of 10 reaches 45%, so the mix only exists in aggregate, which is how
            // ARCHITECTURE.md §8 asks for it to be tested.
            AssertRatio(Seeded(2468), _ => AllAtLevel(0));
        }

        [Test]
        public void TheRatioHolds_AtUnevenCapacities()
        {
            var levels = new Random(8642);
            AssertRatio(Seeded(9753), _ => RandomLevels(levels));
        }

        [Test]
        public void EveryBatchOfTen_HasAtLeastTheFloorOfEachBand()
        {
            // The remainder is random, but the floors hold on every batch: a list is never without
            // a profitable offer.
            var generator = Seeded(1357);

            for (int i = 0; i < 300; i++)
            {
                var batch = Batch(generator, 10);

                Assert.GreaterOrEqual(batch.Count(o => o.Profitability == TradeProfitability.Profitable), 3);
                Assert.GreaterOrEqual(batch.Count(o => o.Profitability == TradeProfitability.Neutral), 4);
                Assert.GreaterOrEqual(batch.Count(o => o.Profitability == TradeProfitability.Unprofitable), 2);
            }
        }

        // ── Offer shape (GDD §7) ──────────────────────────────────────────────────

        [Test]
        public void GiveAndReceiveSets_NeverOverlap()
        {
            foreach (var offer in Batch(Seeded(), 200))
                CollectionAssert.IsEmpty(
                    offer.Give.Select(a => a.Resource).Intersect(offer.Receive.Select(a => a.Resource)),
                    $"Offer {offer.Id} trades a resource for itself.");
        }

        [Test]
        public void EachSide_HasTwoOrThreeResourceTypes()
        {
            foreach (var offer in Batch(Seeded(), 200))
            {
                Assert.That(offer.Give.Count, Is.InRange(2, 3));
                Assert.That(offer.Receive.Count, Is.InRange(2, 3));
            }
        }

        [Test]
        public void EveryAmount_IsAtLeastOne()
        {
            var levels = new Random(31);
            var generator = Seeded();
            for (int i = 0; i < 200; i++)
                foreach (var offer in generator.GenerateBatch(10, Share, RandomLevels(levels)))
                    foreach (var amount in offer.Give.Concat(offer.Receive))
                        Assert.GreaterOrEqual(amount.Units, 1, $"{offer.Id}: {amount}.");
        }

        // ── Sizing (GDD §7) ───────────────────────────────────────────────────────

        [Test]
        public void OffersGrowWithCapacity()
        {
            // The same seed makes the same choices, so each offer is the same deal at a bigger scale.
            var small = Seeded(55).GenerateBatch(10, Share, AllAtLevel(0));
            var large = Seeded(55).GenerateBatch(10, Share, AllAtLevel(10));

            for (int i = 0; i < small.Count; i++)
            {
                Assert.AreEqual(small[i].Profitability, large[i].Profitability);
                Assert.Greater(large[i].TotalGive, small[i].TotalGive * 40, $"Offer {i} did not scale.");
            }
        }

        [TestCase(0.20f)]
        [TestCase(TradeConfig.MaxOfferCapacityShare)]
        public void EveryAmount_StaysWithinItsBound_OfItsOwnWarehouse(float share)
        {
            // Give ≤ share × 5/3 of its warehouse, receive ≤ share × 1.4 × 5/3 (+ rounding). At the
            // largest allowed share that still keeps every amount inside its own warehouse.
            var levels    = new Random(2024);
            var generator = Seeded(4048);

            for (int batch = 0; batch < 2000; batch++)
            {
                var capacities = RandomLevels(levels);
                foreach (var offer in generator.GenerateBatch(10, share, capacities))
                {
                    foreach (var amount in offer.Give)
                    {
                        int cap = capacities[amount.Resource];
                        Assert.LessOrEqual(amount.Units, share * 5d / 3d * cap + 3, $"{offer.Id} gives {amount} of {cap}.");
                        Assert.LessOrEqual(amount.Units, cap, $"{offer.Id} asks more {amount.Resource} than the warehouse holds.");
                    }
                    foreach (var amount in offer.Receive)
                    {
                        int cap = capacities[amount.Resource];
                        Assert.LessOrEqual(amount.Units, share * 1.4d * 5d / 3d * cap + 4, $"{offer.Id} sends {amount} into {cap}.");
                        Assert.LessOrEqual(amount.Units, cap, $"{offer.Id} sends more {amount.Resource} than an empty warehouse holds.");
                    }
                }
            }
        }

        // ── Profitability actually means something ────────────────────────────────

        [Test]
        public void Profitable_ReturnsMoreThanItAsks()
        {
            foreach (var offer in Batch(Seeded(), 400).Where(o => o.Profitability == TradeProfitability.Profitable))
                Assert.Greater(offer.TotalReceive, offer.TotalGive, $"Offer {offer.Id} is not profitable.");
        }

        [Test]
        public void Unprofitable_ReturnsLessThanItAsks()
        {
            foreach (var offer in Batch(Seeded(), 400).Where(o => o.Profitability == TradeProfitability.Unprofitable))
                Assert.Less(offer.TotalReceive, offer.TotalGive, $"Offer {offer.Id} is not unprofitable.");
        }

        [Test]
        public void Neutral_IsRoughlyEven()
        {
            foreach (var offer in Batch(Seeded(), 400).Where(o => o.Profitability == TradeProfitability.Neutral))
            {
                double ratio = offer.TotalReceive / (double)offer.TotalGive;
                // 95–105% by design, plus a unit of rounding on the smallest (40-unit) offers.
                Assert.That(ratio, Is.InRange(0.92d, 1.08d), $"Offer {offer.Id} ratio {ratio}.");
            }
        }

        [Test]
        public void EveryOffer_HasAUniqueId()
        {
            var ids = Batch(Seeded(), 500).Select(o => o.Id).ToList();

            Assert.AreEqual(ids.Count, ids.Distinct().Count(), "Duplicate offer id.");
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static void AssertRatio(TradeOfferGenerator generator,
                                        Func<int, Dictionary<TradeResourceType, int>> capacitiesForBatch)
        {
            var counts = new Dictionary<TradeProfitability, int>
            {
                [TradeProfitability.Profitable]   = 0,
                [TradeProfitability.Neutral]      = 0,
                [TradeProfitability.Unprofitable] = 0
            };

            const int batches = 2000, perBatch = 10;
            for (int i = 0; i < batches; i++)
                foreach (var offer in generator.GenerateBatch(perBatch, Share, capacitiesForBatch(i)))
                    counts[offer.Profitability]++;

            // σ ≈ 0.0011 per band, so 0.015 is ~13σ: it cannot flake, and the generator is seeded.
            double total = batches * perBatch;
            Assert.AreEqual(0.30d, counts[TradeProfitability.Profitable]   / total, 0.015d);
            Assert.AreEqual(0.45d, counts[TradeProfitability.Neutral]      / total, 0.015d);
            Assert.AreEqual(0.25d, counts[TradeProfitability.Unprofitable] / total, 0.015d);
        }
    }
}
