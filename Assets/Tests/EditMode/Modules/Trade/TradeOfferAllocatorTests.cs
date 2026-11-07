using System;
using System.Linq;
using NUnit.Framework;
using KingdomRuler.Modules.Trade;

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
                double scale  = total / targets.Sum();
                var    scaled = targets.Select(t => t * scale).ToArray();

                Assert.AreEqual(total, TradeOfferAllocator.ApportionLargestRemainder(scaled, total).Sum(),
                    $"Parts did not sum to {total}.");
            }
        }

        [Test]
        public void Stochastic_SumsExactly_OnEverySingleBatch()
        {
            // Not just on average: per-index coin flips would get the mean right but let a batch of
            // 10 come out as 9 or 11.
            var rng     = new Random(1234);
            var targets = new[] { 3.0d, 4.5d, 2.5d };

            for (int i = 0; i < 500; i++)
                Assert.AreEqual(10, TradeOfferAllocator.ApportionStochastic(targets, 10, rng).Sum());
        }

        [Test]
        public void Stochastic_HitsTheTargetInExpectation()
        {
            var rng     = new Random(99);
            var targets = new[] { 3.0d, 4.5d, 2.5d };
            var totals  = new int[3];

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

        [Test]
        public void NoTargets_GiveNoParts()
        {
            Assert.IsEmpty(TradeOfferAllocator.ApportionLargestRemainder(new double[0], 5));
            Assert.IsEmpty(TradeOfferAllocator.ApportionStochastic(new double[0], 5, new Random(1)));
        }
    }
}
