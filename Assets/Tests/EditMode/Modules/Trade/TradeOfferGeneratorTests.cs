using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Modules.Trade.Domain;

namespace KingdomRuler.Tests.EditMode.Modules.Trade
{
    [TestFixture]
    public sealed class TradeOfferAllocatorTests
    {
        [Test]
        public void LargestRemainder_SumsExactly_ForEveryCount()
        {
            var targets = new[] { 1.4d, 2.6d, 3.5d };

            for (int total = 0; total <= 100; total++)
            {
                double scale = total / targets.Sum();
                var scaled = targets.Select(t => t * scale).ToArray();

                var parts = TradeOfferAllocator.ApportionLargestRemainder(scaled, total);

                Assert.AreEqual(total, parts.Sum(), $"Parts did not sum to {total}.");
            }
        }

        [Test]
        public void Stochastic_SumsExactly_OnEverySingleBatch()
        {
            // Not just on average: independent per-index coin flips would give the right mean but
            // a varying batch size, so some batches would hold 9 or 11 offers.
            var rng = new Random(1234);
            var targets = new[] { 3.0d, 4.5d, 2.5d };

            for (int i = 0; i < 500; i++)
                Assert.AreEqual(10, TradeOfferAllocator.ApportionStochastic(targets, 10, rng).Sum());
        }

        [Test]
        public void Stochastic_HitsTheTargetInExpectation()
        {
            var rng = new Random(99);
            var targets = new[] { 3.0d, 4.5d, 2.5d };
            var totals = new int[3];

            const int batches = 4000;
            for (int i = 0; i < batches; i++)
            {
                var parts = TradeOfferAllocator.ApportionStochastic(targets, 10, rng);
                for (int j = 0; j < 3; j++) totals[j] += parts[j];
            }

            Assert.AreEqual(3.0d, totals[0] / (double)batches, 0.05d);
            Assert.AreEqual(4.5d, totals[1] / (double)batches, 0.05d);
            Assert.AreEqual(2.5d, totals[2] / (double)batches, 0.05d);
        }
    }

    [TestFixture]
    public sealed class TradeOfferGeneratorTests
    {
        private static TradeOfferGenerator Seeded(int seed = 12345) =>
            new TradeOfferGenerator(new Random(seed));

        // ── Batch shape ───────────────────────────────────────────────────────────

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(5)]
        [TestCase(10)]
        [TestCase(100)]
        public void GenerateBatch_ReturnsExactlyTheRequestedCount(int count)
        {
            Assert.AreEqual(count, Seeded().GenerateBatch(count).Count);
        }

        [Test]
        public void GenerateBatch_WithZeroOrNegative_ReturnsEmpty()
        {
            Assert.IsEmpty(Seeded().GenerateBatch(0));
            Assert.IsEmpty(Seeded().GenerateBatch(-5));
        }

        [Test]
        public void SameSeed_ProducesTheSameBatch()
        {
            var first  = Seeded(777).GenerateBatch(10);
            var second = Seeded(777).GenerateBatch(10);

            CollectionAssert.AreEqual(
                first.Select(o => o.Profitability).ToList(),
                second.Select(o => o.Profitability).ToList());
            CollectionAssert.AreEqual(
                first.Select(o => o.TotalGive).ToList(),
                second.Select(o => o.TotalGive).ToList());
        }

        // ── The 30/45/25 ratio ────────────────────────────────────────────────────

        [Test]
        public void Distribution_Matches_30_45_25_AcrossManyBatches()
        {
            // The bug this pins: the old code used Math.Round per band, and banker's rounding
            // turned Round(10 × 0.25) = Round(2.5) into 2 — shipping a permanent 30/50/20 at the
            // exact batch size GDD §7 specifies. No integer split of 10 reaches 45%, so the ratio
            // is only meaningful in aggregate, which is what ARCHITECTURE.md §8 asks to test.
            var generator = Seeded(2468);
            var counts = new Dictionary<TradeProfitability, int>
            {
                [TradeProfitability.Profitable]   = 0,
                [TradeProfitability.Neutral]      = 0,
                [TradeProfitability.Unprofitable] = 0
            };

            const int batches = 2000;
            const int perBatch = 10;
            for (int i = 0; i < batches; i++)
                foreach (var offer in generator.GenerateBatch(perBatch))
                    counts[offer.Profitability]++;

            double total = batches * perBatch;

            // Tolerance is ~13 standard deviations (σ ≈ 0.0011), so this cannot flake — and the
            // generator is seeded anyway. The old implementation would land at 0.50 / 0.20.
            Assert.AreEqual(0.30d, counts[TradeProfitability.Profitable]   / total, 0.015d);
            Assert.AreEqual(0.45d, counts[TradeProfitability.Neutral]      / total, 0.015d);
            Assert.AreEqual(0.25d, counts[TradeProfitability.Unprofitable] / total, 0.015d);
        }

        [Test]
        public void EveryBatchOfTen_HasAtLeastTheFloorOfEachBand()
        {
            // The remainder is random, but the floors are guaranteed on every single batch, so a
            // player never sees a list with no profitable offers at all.
            var generator = Seeded(1357);

            for (int i = 0; i < 300; i++)
            {
                var batch = generator.GenerateBatch(10);

                Assert.GreaterOrEqual(batch.Count(o => o.Profitability == TradeProfitability.Profitable), 3);
                Assert.GreaterOrEqual(batch.Count(o => o.Profitability == TradeProfitability.Neutral), 4);
                Assert.GreaterOrEqual(batch.Count(o => o.Profitability == TradeProfitability.Unprofitable), 2);
            }
        }

        // ── Offer shape (GDD §7) ──────────────────────────────────────────────────

        [Test]
        public void GiveAndReceiveSets_NeverOverlap()
        {
            foreach (var offer in Seeded().GenerateBatch(200))
            {
                var overlap = offer.GiveResources.Keys.Intersect(offer.ReceiveResources.Keys);
                CollectionAssert.IsEmpty(overlap, $"Offer {offer.Id} trades a resource for itself.");
            }
        }

        [Test]
        public void EachSide_HasTwoOrThreeResourceTypes()
        {
            foreach (var offer in Seeded().GenerateBatch(200))
            {
                Assert.That(offer.GiveResources.Count, Is.InRange(2, 3));
                Assert.That(offer.ReceiveResources.Count, Is.InRange(2, 3));
            }
        }

        [Test]
        public void EveryAmount_IsAtLeastOne()
        {
            // The old DistributeAmong could round a share down to zero, producing an offer that
            // listed a resource but asked for none of it. It only failed intermittently because
            // the tests used an unseeded RNG.
            foreach (var offer in Seeded().GenerateBatch(1000))
            {
                foreach (var pair in offer.GiveResources)
                    Assert.GreaterOrEqual(pair.Value, 1f, $"{offer.Id} gives {pair.Value} {pair.Key}.");
                foreach (var pair in offer.ReceiveResources)
                    Assert.GreaterOrEqual(pair.Value, 1f, $"{offer.Id} receives {pair.Value} {pair.Key}.");
            }
        }

        [Test]
        public void EveryAmount_IsAWholeNumber()
        {
            foreach (var offer in Seeded().GenerateBatch(300))
            foreach (var amount in offer.GiveResources.Values.Concat(offer.ReceiveResources.Values))
                Assert.AreEqual(Math.Round(amount), amount, 0.0001f, "Fractional resource amount.");
        }

        // ── Profitability actually means something ────────────────────────────────

        [Test]
        public void Profitable_ReturnsMoreThanItAsks()
        {
            // All six resources are valued 1:1 (GDD §7), so the totals alone decide this.
            foreach (var offer in Seeded().GenerateBatch(400)
                                          .Where(o => o.Profitability == TradeProfitability.Profitable))
                Assert.Greater(offer.TotalReceive, offer.TotalGive, $"Offer {offer.Id} is not profitable.");
        }

        [Test]
        public void Unprofitable_ReturnsLessThanItAsks()
        {
            foreach (var offer in Seeded().GenerateBatch(400)
                                          .Where(o => o.Profitability == TradeProfitability.Unprofitable))
                Assert.Less(offer.TotalReceive, offer.TotalGive, $"Offer {offer.Id} is not unprofitable.");
        }

        [Test]
        public void Neutral_IsRoughlyEven()
        {
            foreach (var offer in Seeded().GenerateBatch(400)
                                          .Where(o => o.Profitability == TradeProfitability.Neutral))
            {
                float ratio = offer.TotalReceive / offer.TotalGive;
                // 95–105% by design, plus a unit of rounding slack on a ~50-unit offer.
                Assert.That(ratio, Is.InRange(0.90f, 1.10f), $"Offer {offer.Id} ratio {ratio}.");
            }
        }

        [Test]
        public void EveryOffer_HasAUniqueId()
        {
            var ids = Seeded().GenerateBatch(500).Select(o => o.Id).ToList();

            Assert.AreEqual(ids.Count, ids.Distinct().Count(), "Duplicate offer id.");
        }

        [Test]
        public void Offers_AreImmutableToCallers()
        {
            var offer = Seeded().GenerateBatch(1)[0];

            // The dictionaries are IReadOnlyDictionary, so a Presenter holding a live offer
            // cannot rewrite the terms of a trade the Manager is about to execute.
            Assert.IsInstanceOf<IReadOnlyDictionary<TradeResourceType, float>>(offer.GiveResources);
            Assert.IsInstanceOf<IReadOnlyDictionary<TradeResourceType, float>>(offer.ReceiveResources);
        }
    }
}
