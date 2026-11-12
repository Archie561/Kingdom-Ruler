using System;

namespace KingdomRuler.Shared.Accrual
{
    /// <summary>
    /// A store that fills from empty to full over a fixed period, one whole unit at a time: a Trade
    /// warehouse, an Economy business storage. Pure and static — the caller owns the state.
    /// </summary>
    /// <remarks>
    /// <para><b>No fraction is stored.</b> A store of capacity <c>c</c> gains one unit every
    /// <c>period ÷ c</c>, so an empty one is full after exactly one period at any size. Progress toward
    /// the next unit lives in the start timestamp: it is the moment the unit now accruing started, and
    /// it advances by whole units' worth of time. Many short settles and one long one give the same
    /// result, and an amount plus that timestamp is everything a save needs.</para>
    ///
    /// <para><b>Exact at any capacity.</b> <c>elapsed ticks × capacity</c> leaves <c>long</c> once
    /// capacity passes ~10⁷ for a one-day period, and a gold storage is far larger. Products that would
    /// overflow go through a 128-bit multiply-divide instead — not <c>double</c>, which is inexact, and
    /// not <c>BigInteger</c>, which allocates on every clock tick. Every input is a non-negative
    /// <c>long</c> and every result fits one, so the arithmetic is exact across the whole range.</para>
    ///
    /// <para>A capacity of zero or less is a store that produces nothing — a business type with no
    /// units owned — and counts as always full.</para>
    /// </remarks>
    public static class WholeUnitAccrual
    {
        /// <summary>
        /// Credit every whole unit accrued by <paramref name="now"/>. Returns the new amount; the new
        /// start of the unit in progress comes out through <paramref name="startedUtcAfter"/>.
        /// </summary>
        /// <remarks>
        /// <para>A start in the future <b>resynchronises</b> to <paramref name="now"/>: nothing else
        /// moves it backwards, so a device clock that ran ahead and was corrected would otherwise stall
        /// the store until that date. A full store banks nothing, so its start follows the clock.</para>
        ///
        /// <para><b>Rounding favours the player.</b> The new start is floored to a whole tick, so it can
        /// only land earlier than the exact unit boundary, by under one tick, and the next settle counts
        /// that sliver again. Progress is never lost. A store settled often therefore runs ahead of one
        /// settled once by under one tick of production per settle — under one part in 2.5 million at
        /// the game's 4 Hz, at any capacity. While a unit spans many ticks (every warehouse) that rounds
        /// to nothing; once a tick holds whole units (a gold storage near the ceiling) it shows as a few
        /// units ahead. Exact equality there would need a stored sub-tick fraction, which nothing in
        /// play comes near needing.</para>
        /// </remarks>
        public static long Settle(long amount, long capacity, DateTime startedUtc, TimeSpan period, DateTime now,
                                  out DateTime startedUtcAfter)
        {
            long periodTicks = PeriodTicks(period);

            if (now <= startedUtc || amount >= capacity)
            {
                startedUtcAfter = now;
                return amount;
            }

            long elapsed = (now - startedUtc).Ticks;
            if (elapsed >= periodTicks)
            {
                startedUtcAfter = now;
                return capacity;
            }

            // elapsed < period, so units < capacity: the quotient fits.
            long units = MulDiv(elapsed, capacity, periodTicks, out _);
            if (units == 0)
            {
                startedUtcAfter = startedUtc;
                return amount;
            }

            long settled = amount + Math.Min(units, capacity - amount);
            startedUtcAfter = settled >= capacity
                ? now
                : startedUtc.AddTicks(MulDiv(units, periodTicks, capacity, out _));   // units < capacity, so < period
            return settled;
        }

        /// <summary>Progress toward the next unit at <paramref name="now"/>, 0..1. Zero while full.</summary>
        public static float NextUnitProgress(long amount, long capacity, DateTime startedUtc, TimeSpan period, DateTime now)
        {
            long periodTicks = PeriodTicks(period);
            if (amount >= capacity || now <= startedUtc) return 0f;

            MulDiv(ElapsedTicks(startedUtc, periodTicks, now), capacity, periodTicks, out long intoUnit);
            return (float)(intoUnit / (double)periodTicks);
        }

        /// <summary>How long until the store is full, from <paramref name="now"/>. Zero when it already is.</summary>
        public static TimeSpan TimeUntilFull(long amount, long capacity, DateTime startedUtc, TimeSpan period, DateTime now)
        {
            long periodTicks = PeriodTicks(period);
            if (amount >= capacity) return TimeSpan.Zero;

            // The k-th missing unit lands once elapsed × capacity ≥ k × period, so round the wait up.
            // missing ≤ capacity, so the quotient is at most one period.
            long needed  = MulDiv(capacity - amount, periodTicks, capacity, out long remainder);
            if (remainder > 0) needed++;

            long elapsed = now > startedUtc ? ElapsedTicks(startedUtc, periodTicks, now) : 0L;
            return TimeSpan.FromTicks(Math.Max(0L, needed - elapsed));
        }

        /// <summary>How long one unit takes at this capacity. <see cref="TimeSpan.MaxValue"/> for a store that produces nothing.</summary>
        public static TimeSpan TimePerUnit(long capacity, TimeSpan period)
        {
            long periodTicks = PeriodTicks(period);
            return capacity > 0 ? TimeSpan.FromTicks(periodTicks / capacity) : TimeSpan.MaxValue;
        }

        // ── Private ───────────────────────────────────────────────────────────────

        private static long PeriodTicks(TimeSpan period)
        {
            if (period.Ticks <= 0) throw new ArgumentOutOfRangeException(nameof(period), period, "The fill period must be positive.");
            return period.Ticks;
        }

        private static long ElapsedTicks(DateTime startedUtc, long periodTicks, DateTime now) =>
            Math.Min((now - startedUtc).Ticks, periodTicks);

        /// <summary>
        /// ⌊a × b ÷ divisor⌋ and its remainder, exact for non-negative <paramref name="a"/> and
        /// <paramref name="b"/> and a positive <paramref name="divisor"/>. The quotient must fit a
        /// <c>long</c>; every caller bounds it.
        /// </summary>
        private static long MulDiv(long a, long b, long divisor, out long remainder)
        {
            if (a == 0 || b == 0)
            {
                remainder = 0;
                return 0;
            }

            // The common case — every Trade warehouse — stays in plain long arithmetic.
            if (a <= long.MaxValue / b)
            {
                long product = a * b;
                remainder = product % divisor;
                return product / divisor;
            }

            Multiply((ulong)a, (ulong)b, out ulong high, out ulong low);
            ulong quotient = Divide(high, low, (ulong)divisor, out ulong rest);
            if (quotient > long.MaxValue) throw new OverflowException("WholeUnitAccrual: quotient outside long.");

            remainder = (long)rest;
            return (long)quotient;
        }

        /// <summary>The full 128-bit product of two 64-bit values, from four 32-bit partial products.</summary>
        private static void Multiply(ulong a, ulong b, out ulong high, out ulong low)
        {
            ulong aLow = a & 0xFFFFFFFFUL, aHigh = a >> 32;
            ulong bLow = b & 0xFFFFFFFFUL, bHigh = b >> 32;

            ulong lowLow   = aLow  * bLow;
            ulong lowHigh  = aLow  * bHigh;
            ulong highLow  = aHigh * bLow;
            ulong highHigh = aHigh * bHigh;

            ulong middle = (lowLow >> 32) + (lowHigh & 0xFFFFFFFFUL) + (highLow & 0xFFFFFFFFUL);
            low  = (middle << 32) | (lowLow & 0xFFFFFFFFUL);
            high = highHigh + (lowHigh >> 32) + (highLow >> 32) + (middle >> 32);
        }

        /// <summary>A 128-bit value divided by a 64-bit one, by shift and subtract. Requires <c>high &lt; divisor</c>, so the quotient fits 64 bits.</summary>
        private static ulong Divide(ulong high, ulong low, ulong divisor, out ulong remainder)
        {
            if (high >= divisor) throw new OverflowException("WholeUnitAccrual: quotient outside 64 bits.");

            ulong rest     = high;
            ulong quotient = 0;
            for (int bit = 63; bit >= 0; bit--)
            {
                // rest < divisor before the shift, so 2 × rest + 1 < 2 × divisor: one subtraction is
                // enough. When the shift carries out of 64 bits the true value exceeds the divisor,
                // and the wrapped subtraction still gives the right remainder.
                bool carry = (rest >> 63) != 0;
                rest     = (rest << 1) | ((low >> bit) & 1UL);
                quotient <<= 1;

                if (carry || rest >= divisor)
                {
                    rest     -= divisor;
                    quotient |= 1UL;
                }
            }

            remainder = rest;
            return quotient;
        }
    }
}
