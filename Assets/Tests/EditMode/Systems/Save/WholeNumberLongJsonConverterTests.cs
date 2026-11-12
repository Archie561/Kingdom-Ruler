using NUnit.Framework;
using Newtonsoft.Json;
using KingdomRuler.Systems.Save;

namespace KingdomRuler.Tests.EditMode.Systems
{
    /// <summary>
    /// Gold amounts are <c>long</c>: a file holding a decimal, a non-finite or an out-of-range number
    /// must still load — clamped by the field's own load afterwards — rather than set the whole save aside.
    /// </summary>
    [TestFixture]
    public sealed class WholeNumberLongJsonConverterTests
    {
        private static long Read(string number) =>
            JsonConvert.DeserializeObject<BusinessStateDto>(@"{ ""stored"": " + number + " }").Stored;

        [TestCase("25",                    25L)]
        [TestCase("999999999999999",       999_999_999_999_999L)]
        [TestCase("25.0",                  25L)]
        [TestCase("87.34",                 87L)]
        [TestCase("87.5",                  88L)]   // halves away from zero, like the int converter
        [TestCase("-3",                    -3L)]   // kept here; each field's own load clamps it
        [TestCase("NaN",                   0L)]
        [TestCase("Infinity",              0L)]
        [TestCase("null",                  0L)]
        public void Reads_WholeNumbersDecimalsAndNonFinite(string json, long expected)
        {
            Assert.AreEqual(expected, Read(json));
        }

        [TestCase("99999999999999999999999")]   // beyond long: Newtonsoft hands over a BigInteger
        [TestCase("1e30")]
        [TestCase("9223372036854775807")]
        public void ANumberBeyondLong_Saturates(string json)
        {
            Assert.AreEqual(long.MaxValue, Read(json));
        }

        [TestCase("-99999999999999999999999")]
        [TestCase("-1e30")]
        public void ANegativeNumberBeyondLong_Saturates(string json)
        {
            Assert.AreEqual(long.MinValue, Read(json));
        }

        [Test]
        public void Writes_AWholeNumber()
        {
            var json = JsonConvert.SerializeObject(new BusinessStateDto { Stored = 1_234_567_890_123 });

            StringAssert.Contains(@"""stored"":1234567890123", json);
            StringAssert.DoesNotContain(@"1234567890123.0", json);
        }
    }
}
