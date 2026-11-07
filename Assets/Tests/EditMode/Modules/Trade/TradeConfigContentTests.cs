using NUnit.Framework;
using KingdomRuler.Modules.Trade;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Tests.EditMode.Systems;

namespace KingdomRuler.Tests.EditMode.Modules.Trade
{
    /// <summary>
    /// Checks the <b>real</b> <c>TradeConfig.asset</c>: whatever it is tuned to, the curves must stay
    /// meaningful all the way to the level cap and every number must fit an <c>int</c>.
    /// </summary>
    /// <remarks>
    /// The curve clamps rather than overflows, so a bad tuning would not crash; it would silently
    /// plateau at the Ledger's ceiling and stop the top levels doing anything. This catches it.
    /// </remarks>
    public sealed class TradeConfigContentTests
    {
        private static TradeConfig Config => ProjectAssets.TheOnly<TradeConfig>();

        [Test]
        public void CapacityAtTheCap_StaysUnderTheLedgerCeiling()
        {
            var config = Config;
            Assert.Less(WarehouseCurve.Capacity(config, WarehouseCurve.TopLevel(config)), KingdomLedger.MaxWarehouseCapacity,
                $"{config.name}: capacity reaches the Ledger's ceiling by level {config.MaxWarehouseLevel}; " +
                "lower the growth or the cap.");
        }

        [Test]
        public void CurvesIncreaseStrictly_UpToTheCap()
        {
            var config = Config;
            int top    = WarehouseCurve.TopLevel(config);
            for (int level = 1; level <= top; level++)
                Assert.Greater(WarehouseCurve.Capacity(config, level), WarehouseCurve.Capacity(config, level - 1),
                    $"{config.name}: capacity does not grow at level {level}.");

            // The top level has no upgrade out of it, so no price.
            for (int level = 1; level < top; level++)
                Assert.Greater(WarehouseCurve.CrystalCost(config, level), WarehouseCurve.CrystalCost(config, level - 1),
                    $"{config.name}: the crystal price does not rise at level {level}.");
        }

        [Test]
        public void TheLargestPossibleOffer_FitsAnInt()
        {
            // Three warehouses at the cap, the largest allowed share, the best multiplier.
            var config  = Config;
            long give    = (long)(TradeConfig.MaxOfferCapacityShare * 3d * WarehouseCurve.Capacity(config, WarehouseCurve.TopLevel(config))) + 1;
            long receive = (long)(give * 1.40d) + 1;

            Assert.Less(receive, int.MaxValue);
        }
    }
}
