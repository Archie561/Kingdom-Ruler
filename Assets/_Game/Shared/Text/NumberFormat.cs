using System;
using System.Globalization;

namespace KingdomRuler.Shared.Text
{
    /// <summary>
    /// How this game writes big whole numbers — gold first (GDD §8) — in one place, so an amount reads
    /// the same everywhere.
    /// </summary>
    /// <remarks>
    /// <para><b>The scheme.</b> Below 10,000 a number is written in full, grouped: <c>9,999</c>. From
    /// there it is abbreviated to three significant digits with a suffix: <c>12.3K</c>, <c>123K</c>,
    /// <c>1.23M</c> … <c>999T</c>, trailing zeros dropped (<c>10K</c>, <c>1.2M</c>). Exact up to 9,999
    /// because early prices and storages live there and the player counts them coin by coin.</para>
    ///
    /// <para><b>Truncated, never rounded up</b>: <c>1,999,999</c> is <c>1.99M</c>, not <c>2M</c>. A
    /// displayed amount must never overstate what the player holds — a balance shown as 2M that cannot
    /// buy a 2M business reads as a bug.</para>
    ///
    /// <para>The decimal separator follows the culture passed in; the suffixes are passed in too,
    /// already localized (<see cref="NumberSuffixes"/>). Trillions are the last suffix: the gold
    /// ceiling sits just under 1,000T, and anything beyond still shows, grouped, as <c>9,223,372T</c>.</para>
    /// </remarks>
    public static class NumberFormat
    {
        /// <summary>The smallest number written abbreviated; everything below is written in full.</summary>
        public const long AbbreviateFrom = 10_000;

        private static readonly long[] Magnitudes = { 1_000L, 1_000_000L, 1_000_000_000L, 1_000_000_000_000L };

        /// <summary>"9,999", "12.3K", "1.23M" — see the class remarks for the rules.</summary>
        public static string Abbreviate(long value, NumberSuffixes suffixes, CultureInfo culture)
        {
            culture ??= CultureInfo.CurrentCulture;
            if (!IsAbbreviated(value)) return value.ToString("N0", culture);

            int  magnitude = MagnitudeOf(value);
            long unit      = Magnitudes[magnitude];

            // Exact: the displayed value is a whole multiple of a power of ten no larger than the unit.
            decimal mantissa = (decimal)DisplayedValue(value) / unit;
            string  number   = mantissa.ToString(FormatFor(mantissa), culture);

            return string.Format(culture, suffixes.FormatFor(magnitude), number);
        }

        /// <summary>
        /// <paramref name="value"/> with the digits the abbreviation drops set to zero: 12,345,678 shows
        /// as 12.3M, so this is 12,300,000. Two values display the same exactly when this is equal, so a
        /// view polling every frame can skip re-formatting while the text would not change.
        /// </summary>
        public static long DisplayedValue(long value)
        {
            if (!IsAbbreviated(value)) return value;

            long unit = Magnitudes[MagnitudeOf(value)];
            long step = Step(value, unit);
            return value / step * step;   // integer division truncates toward zero
        }

        // ── Private ───────────────────────────────────────────────────────────────

        private static bool IsAbbreviated(long value) => value >= AbbreviateFrom || value <= -AbbreviateFrom;

        /// <summary>The largest magnitude the value reaches: 0 thousands … 3 trillions.</summary>
        private static int MagnitudeOf(long value)
        {
            for (int i = Magnitudes.Length - 1; i > 0; i--)
                if (value >= Magnitudes[i] || value <= -Magnitudes[i]) return i;
            return 0;
        }

        /// <summary>The value of the last digit shown: three significant digits, fewer once past 999 of the largest unit.</summary>
        private static long Step(long value, long unit)
        {
            // Compared as quotients, so long.MinValue needs no absolute value.
            long whole = Math.Abs(value / unit);
            if (whole < 10)  return unit / 100;
            if (whole < 100) return unit / 10;
            return unit;
        }

        private static string FormatFor(decimal mantissa)
        {
            decimal whole = Math.Abs(decimal.Truncate(mantissa));
            if (whole < 10)  return "#,0.##";
            if (whole < 100) return "#,0.#";
            return "#,0";
        }
    }
}
