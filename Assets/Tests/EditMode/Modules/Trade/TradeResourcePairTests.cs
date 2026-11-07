using System;
using NUnit.Framework;
using KingdomRuler.Modules.Trade;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Tests.EditMode.Modules.Trade
{
    [TestFixture]
    public sealed class TradeResourcePairTests
    {
        [TestCase(TradeResourceType.Stone,    TradeResourceType.Wood)]
        [TestCase(TradeResourceType.Wood,     TradeResourceType.Stone)]
        [TestCase(TradeResourceType.Metal,    TradeResourceType.Minerals)]
        [TestCase(TradeResourceType.Minerals, TradeResourceType.Metal)]
        [TestCase(TradeResourceType.Leather,  TradeResourceType.Clay)]
        [TestCase(TradeResourceType.Clay,     TradeResourceType.Leather)]
        public void PairedWith_MatchesTheGddPairing(TradeResourceType from, TradeResourceType to)
        {
            Assert.AreEqual(to, TradeResourcePair.PairedWith(from));
        }

        [Test]
        public void Pairing_IsSymmetric_AndNeverSelf()
        {
            foreach (TradeResourceType type in Enum.GetValues(typeof(TradeResourceType)))
            {
                var paired = TradeResourcePair.PairedWith(type);
                Assert.AreEqual(type, TradeResourcePair.PairedWith(paired));
                Assert.AreNotEqual(type, paired, "A resource cannot pair with itself.");
            }
        }
    }
}
