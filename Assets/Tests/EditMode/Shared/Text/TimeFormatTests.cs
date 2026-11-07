using NUnit.Framework;
using KingdomRuler.Shared.Text;

namespace KingdomRuler.Tests.EditMode.Shared.Text
{
    [TestFixture]
    public sealed class TimeFormatTests
    {
        [TestCase(0,    "0:00")]
        [TestCase(59,   "0:59")]
        [TestCase(61,   "1:01")]
        [TestCase(1200, "20:00")]
        [TestCase(-5,   "0:00")]
        public void MinutesSeconds_FormatsAndClampsNegative(int seconds, string expected)
        {
            Assert.AreEqual(expected, TimeFormat.MinutesSeconds(seconds));
        }

        [TestCase(0,     "0:00:00")]
        [TestCase(59,    "0:00:59")]
        [TestCase(3600,  "1:00:00")]
        [TestCase(18723, "5:12:03")]
        [TestCase(86400, "24:00:00")]
        [TestCase(-5,    "0:00:00")]
        public void HoursMinutesSeconds_FormatsAndClampsNegative(int seconds, string expected)
        {
            Assert.AreEqual(expected, TimeFormat.HoursMinutesSeconds(seconds));
        }
    }
}
