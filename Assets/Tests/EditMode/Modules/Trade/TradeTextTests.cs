using NUnit.Framework;
using KingdomRuler.Modules.Trade;
using KingdomRuler.Tests.EditMode.Systems;

namespace KingdomRuler.Tests.EditMode.Modules.Trade
{
    /// <summary>
    /// Checks that every message Trade builds in code exists in the real String Table.
    /// </summary>
    /// <remarks>
    /// These are the strings a gap hides in: an overflow warning or an "offer unavailable"
    /// blocker appears only in specific situations. A key in no locale fails; an untranslated
    /// one only warns (<c>ARCHITECTURE.md</c> §8). The screen's fixed labels are not checked —
    /// they are on screen the moment the Trade tab opens.
    /// </remarks>
    public sealed class TradeTextTests
    {
        [Test]
        public void EveryMessageBuiltInCode_ExistsInEveryLocale()
        {
            var text = new LocalizationCheck(TradeUIText.StringTable);
            foreach (var key in TradeUIText.AllKeys) text.Require(key);
            text.Report();
        }
    }
}
