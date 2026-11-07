using NUnit.Framework;
using KingdomRuler.Modules.Trade.UI;
using KingdomRuler.Tests.EditMode.Systems;

namespace KingdomRuler.Tests.EditMode.Modules.Trade
{
    /// <summary>Every message Trade builds in code exists in the real String Table.</summary>
    /// <remarks>
    /// These are the strings a gap hides in — an overflow warning or a "not enough" notice appears
    /// only in particular states. A key in no locale fails; an untranslated one only warns
    /// (<c>ARCHITECTURE.md</c> §8). Fixed chrome is not checked: it is on screen with its prefab.
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
