using System;
using System.Collections.Generic;
using NUnit.Framework;
using KingdomRuler.Modules.Trade;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Tests.EditMode.Modules.Trade
{
    /// <summary>An offer's own guarantees, and the rule that an overflow warns rather than blocks.</summary>
    [TestFixture]
    public sealed class TradeOfferTests
    {
        private static TradeAmount A(TradeResourceType resource, int units) => new TradeAmount(resource, units);

        private static readonly TradeAmount[] Give    = { A(TradeResourceType.Stone, 20), A(TradeResourceType.Wood, 15) };
        private static readonly TradeAmount[] Receive = { A(TradeResourceType.Clay, 30), A(TradeResourceType.Metal, 12) };

        [Test]
        public void Totals_AreTheSumOfEachSide()
        {
            var offer = new TradeOffer("a", Give, Receive, TradeProfitability.Profitable);

            Assert.AreEqual(35, offer.TotalGive);
            Assert.AreEqual(42, offer.TotalReceive);
        }

        [Test]
        public void TheTerms_CannotBeChangedFromOutside()
        {
            var give  = new List<TradeAmount>(Give);
            var offer = new TradeOffer("a", give, Receive, TradeProfitability.Neutral);

            give[0] = A(TradeResourceType.Stone, 9999);

            Assert.AreEqual(20, offer.Give[0].Units, "The offer must copy its inputs.");
            Assert.IsFalse(offer.Give is TradeAmount[], "An exposed array could be cast back and written.");
        }

        [Test]
        public void TryCreate_RefusesDamagedTerms()
        {
            Assert.IsFalse(TradeOffer.TryCreate("a", new TradeAmount[0], Receive, TradeProfitability.Neutral, out _), "empty side");
            Assert.IsFalse(TradeOffer.TryCreate("", Give, Receive, TradeProfitability.Neutral, out _), "no id");
            Assert.IsFalse(TradeOffer.TryCreate("a", new[] { A(TradeResourceType.Stone, 0) }, Receive,
                TradeProfitability.Neutral, out _), "zero amount");
            Assert.IsFalse(TradeOffer.TryCreate("a", Give, new[] { A(TradeResourceType.Stone, 5) },
                TradeProfitability.Neutral, out _), "a resource on both sides");
            Assert.IsFalse(TradeOffer.TryCreate("a", new[] { A(TradeResourceType.Stone, 5), A(TradeResourceType.Stone, 5) },
                Receive, TradeProfitability.Neutral, out _), "a resource twice on one side");

            Assert.IsTrue(TradeOffer.TryCreate("a", Give, Receive, TradeProfitability.Neutral, out var offer));
            Assert.AreEqual("a", offer.Id);
        }

        [Test]
        public void TheConstructor_ThrowsOnDamagedTerms()
        {
            Assert.Throws<ArgumentException>(() =>
                new TradeOffer("a", Give, new[] { A(TradeResourceType.Wood, 5) }, TradeProfitability.Neutral));
        }

        // ── Overflow warns, it does not block ─────────────────────────────────────

        [Test]
        public void AnOverflow_DoesNotBlock_ButAShortfallDoes()
        {
            Assert.IsFalse(new TradeIssue(TradeIssueKind.WouldOverflowWarehouse, TradeResourceType.Clay, 5).BlocksAcceptance);
            Assert.IsTrue(new TradeIssue(TradeIssueKind.InsufficientResource, TradeResourceType.Stone, 5).BlocksAcceptance);
            Assert.IsTrue(TradeIssue.Unavailable().BlocksAcceptance);
        }

        [Test]
        public void AnEvaluation_WithOnlyAWarning_CanStillBeAccepted()
        {
            var warned = new TradeOfferEvaluation(true,
                new[] { new TradeIssue(TradeIssueKind.WouldOverflowWarehouse, TradeResourceType.Clay, 5) });

            Assert.IsTrue(warned.CanAccept);
        }

        [Test]
        public void AnEvaluation_WithAShortfall_CannotBeAccepted()
        {
            var blocked = new TradeOfferEvaluation(true, new[]
            {
                new TradeIssue(TradeIssueKind.WouldOverflowWarehouse, TradeResourceType.Clay, 5),
                new TradeIssue(TradeIssueKind.InsufficientResource, TradeResourceType.Stone, 3)
            });

            Assert.IsFalse(blocked.CanAccept);
        }

        [Test]
        public void AMissingOffer_CannotBeAccepted_AndSaysWhy()
        {
            var missing = TradeOfferEvaluation.Missing();

            Assert.IsFalse(missing.CanAccept);
            Assert.AreEqual(TradeIssueKind.OfferUnavailable, missing.Issues[0].Kind);
        }

        [Test]
        public void ARefusedResult_ReceivedAndForfeitedNothing()
        {
            var refused = TradeAcceptResult.Refused(new[] { TradeIssue.Unavailable() });

            Assert.IsFalse(refused.Accepted);
            Assert.IsEmpty(refused.Received);
            Assert.IsEmpty(refused.Forfeited);
            Assert.AreEqual(1, refused.Issues.Count);
        }
    }
}
