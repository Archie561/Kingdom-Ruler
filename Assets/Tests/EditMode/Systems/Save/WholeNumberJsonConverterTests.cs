using NUnit.Framework;
using Newtonsoft.Json;
using KingdomRuler.Systems.Save;

namespace KingdomRuler.Tests.EditMode.Systems
{
    /// <summary>
    /// Whole-number <c>int</c> fields: a decimal, a non-finite or an out-of-range number must still load —
    /// clamped by the field's own load afterwards — rather than set the whole save aside.
    /// </summary>
    [TestFixture]
    public sealed class WholeNumberJsonConverterTests
    {
        private static int Read(string number) =>
            JsonConvert.DeserializeObject<TradeResourceStateDto>(@"{ ""amount"": " + number + " }").Amount;

        [TestCase("25",       25)]
        [TestCase("25.0",     25)]
        [TestCase("87.34",    87)]
        [TestCase("87.5",     88)]   // halves away from zero
        [TestCase("-3",       -3)]   // kept here; each field's own load clamps it
        [TestCase("NaN",      0)]
        [TestCase("Infinity", 0)]
        [TestCase("null",     0)]
        public void Reads_WholeNumbersDecimalsAndNonFinite(string json, int expected)
        {
            Assert.AreEqual(expected, Read(json));
        }

        [TestCase("5000000000")]                 // beyond int, inside long
        [TestCase("99999999999999999999999")]    // beyond long: Newtonsoft hands over a BigInteger
        [TestCase("1e30")]
        [TestCase("2147483647")]
        public void ANumberBeyondInt_Saturates(string json)
        {
            Assert.AreEqual(int.MaxValue, Read(json));
        }

        [TestCase("-5000000000")]
        [TestCase("-99999999999999999999999")]
        public void ANegativeNumberBeyondInt_Saturates(string json)
        {
            Assert.AreEqual(int.MinValue, Read(json));
        }
    }
}
