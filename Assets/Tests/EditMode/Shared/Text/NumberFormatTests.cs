using System;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using UnityEditor.Localization;
using KingdomRuler.Shared.Text;
using KingdomRuler.Tests.EditMode.Systems;

namespace KingdomRuler.Tests.EditMode.Shared.Text
{
    /// <summary>
    /// Big numbers in full below 10,000, then three significant digits with a suffix — truncated, never
    /// rounded up — in the player's language.
    /// </summary>
    [TestFixture]
    public sealed class NumberFormatTests
    {
        private static readonly CultureInfo English   = CultureInfo.GetCultureInfo("en-US");
        private static readonly CultureInfo Ukrainian = CultureInfo.GetCultureInfo("uk-UA");

        private static readonly NumberSuffixes EnglishSuffixes   = new("{0}K", "{0}M", "{0}B", "{0}T");
        private static readonly NumberSuffixes UkrainianSuffixes = new("{0} тис.", "{0} млн", "{0} млрд", "{0} трлн");

        private static string En(long value) => NumberFormat.Abbreviate(value, EnglishSuffixes, English);

        [TestCase(0L,         "0")]
        [TestCase(7L,         "7")]
        [TestCase(1_440L,     "1,440")]
        [TestCase(9_999L,     "9,999")]   // the last number written in full
        [TestCase(10_000L,    "10K")]     // the first abbreviated; a trailing ".0" is dropped
        [TestCase(12_345L,    "12.3K")]
        [TestCase(99_999L,    "99.9K")]
        [TestCase(100_000L,   "100K")]
        [TestCase(999_999L,   "999K")]
        [TestCase(1_000_000L, "1M")]
        [TestCase(1_200_000L, "1.2M")]
        [TestCase(1_230_000L, "1.23M")]
        [TestCase(12_345_678L, "12.3M")]
        [TestCase(123_456_789L, "123M")]
        [TestCase(1_234_567_890L, "1.23B")]
        [TestCase(1_000_000_000_000L, "1T")]
        [TestCase(40_000_000_000_000L, "40T")]
        public void Abbreviate_FollowsTheScheme(long value, string expected)
        {
            Assert.AreEqual(expected, En(value));
        }

        [TestCase(1_999_999L,      "1.99M")]
        [TestCase(19_999L,         "19.9K")]
        [TestCase(999_999_999L,    "999M")]
        [TestCase(9_999_999_999L,  "9.99B")]
        public void Abbreviate_Truncates_NeverRoundingUp(long value, string expected)
        {
            // A balance shown as 2M must be able to buy a 2M business.
            Assert.AreEqual(expected, En(value));
        }

        [Test]
        public void Abbreviate_AtTheGoldCeiling_And_TheTopOfLong()
        {
            Assert.AreEqual("999T", En(999_999_999_999_999), "KingdomLedger.MaxGold: T is the last suffix ever needed.");
            Assert.AreEqual("9,223,372T", En(long.MaxValue), "Past the ceiling it still shows, grouped.");
        }

        [TestCase(-9_999L,  "-9,999")]
        [TestCase(-12_345L, "-12.3K")]
        [TestCase(-1_999_999L, "-1.99M")]
        public void Abbreviate_NegativeAmounts_TruncateTowardZero(long value, string expected)
        {
            Assert.AreEqual(expected, En(value));
        }

        [Test]
        public void Abbreviate_LongMinValue_DoesNotOverflow()
        {
            Assert.AreEqual("-9,223,372T", En(long.MinValue));
        }

        [Test]
        public void Abbreviate_InUkrainian_UsesItsSeparatorsAndSuffixes()
        {
            string group = Ukrainian.NumberFormat.NumberGroupSeparator;

            Assert.AreEqual("1,23 млн", NumberFormat.Abbreviate(1_230_000, UkrainianSuffixes, Ukrainian));
            Assert.AreEqual("12,3 тис.", NumberFormat.Abbreviate(12_345, UkrainianSuffixes, Ukrainian));
            Assert.AreEqual("9" + group + "999", NumberFormat.Abbreviate(9_999, UkrainianSuffixes, Ukrainian));
        }

        [Test]
        public void ASuffixWithoutItsPlaceholder_StillShowsTheNumber()
        {
            // A missing entry resolves to "[key]": the gap shows, and the amount is not swallowed.
            var broken = new NumberSuffixes("[number.suffix.thousand]", null, "", "{0}T");

            Assert.AreEqual("12.3[number.suffix.thousand]", NumberFormat.Abbreviate(12_345, broken, English));
            Assert.AreEqual("1.2",  NumberFormat.Abbreviate(1_200_000, broken, English));
            Assert.AreEqual("1.23", NumberFormat.Abbreviate(1_234_567_890, broken, English));
        }

        // ── DisplayedValue ────────────────────────────────────────────────────────

        [TestCase(9_999L,          9_999L)]
        [TestCase(12_345L,         12_300L)]
        [TestCase(12_345_678L,     12_300_000L)]
        [TestCase(1_999_999L,      1_990_000L)]
        [TestCase(-12_345L,        -12_300L)]
        public void DisplayedValue_ZeroesTheDigitsTheAbbreviationDrops(long value, long expected)
        {
            Assert.AreEqual(expected, NumberFormat.DisplayedValue(value));
        }

        [Test]
        public void DisplayedValue_IsEqual_ExactlyWhenTheTextIs()
        {
            // What lets a view polling every frame skip formatting until the text would change.
            var rng = new Random(20261112);
            for (int i = 0; i < 5_000; i++)
            {
                long a = (long)(rng.NextDouble() * 2e12) - 1_000_000_000_000L;
                long b = a + rng.Next(-3_000, 3_000) * (long)Math.Pow(10, rng.Next(0, 9));

                bool sameText  = En(a) == En(b);
                bool sameValue = NumberFormat.DisplayedValue(a) == NumberFormat.DisplayedValue(b);
                Assert.AreEqual(sameText, sameValue, $"{a} ({En(a)}) vs {b} ({En(b)})");
            }
        }

        // ── The real String Table ─────────────────────────────────────────────────

        [Test]
        public void EverySuffix_ExistsInEveryLocale()
        {
            var text = new LocalizationCheck(NumberSuffixes.StringTable);
            foreach (var key in NumberSuffixes.AllKeys) text.Require(key);
            text.Report();
        }

        /// <summary>A translation without <c>{0}</c> would show the suffix with no number: a wiring mistake, so it fails.</summary>
        [Test]
        public void EverySuffix_TakesTheNumber_InEveryLocale()
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(NumberSuffixes.StringTable);
            Assert.IsNotNull(collection, $"String table '{NumberSuffixes.StringTable}' does not exist.");

            var problems = new List<string>();
            foreach (var table in collection.StringTables)
            foreach (var key in NumberSuffixes.AllKeys)
            {
                var entry = table.GetEntry(key);
                if (entry != null && !string.IsNullOrEmpty(entry.Value) && !entry.Value.Contains("{0}"))
                    problems.Add($"[{table.LocaleIdentifier.Code}] {key} = \"{entry.Value}\" has no {{0}}.");
            }

            CollectionAssert.IsEmpty(problems, string.Join("\n", problems));
        }
    }
}
