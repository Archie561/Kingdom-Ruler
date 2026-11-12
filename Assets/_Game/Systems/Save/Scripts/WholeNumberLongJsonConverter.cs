using System;
using System.Numerics;
using Newtonsoft.Json;
using UnityEngine.Scripting;

namespace KingdomRuler.Systems.Save
{
    /// <summary>
    /// The <c>long</c> counterpart of <see cref="WholeNumberJsonConverter"/>, for gold amounts: reads
    /// <c>25</c> or <c>25.0</c>, rounding a fraction to the nearest whole number. Without it a single
    /// <c>25.0</c> would set the whole save aside.
    /// </summary>
    /// <remarks>
    /// <para><b>A number outside <c>long</c> is clamped, not rejected.</b> Newtonsoft hands an integer
    /// beyond <c>long</c> over as a <see cref="BigInteger"/>, and an exponent like <c>1e30</c> as a
    /// double; both saturate here, and each field's own load clamps the result to its real range
    /// (the gold ceiling, a storage's capacity). NaN, Infinity and null read as 0.</para>
    ///
    /// <para><c>[Preserve]</c>: Newtonsoft creates it by reflection, which IL2CPP stripping cannot see.</para>
    /// </remarks>
    [Preserve]
    public sealed class WholeNumberLongJsonConverter : JsonConverter<long>
    {
        [Preserve]
        public WholeNumberLongJsonConverter() { }

        public override long ReadJson(JsonReader reader, Type objectType, long existingValue,
                                      bool hasExistingValue, JsonSerializer serializer)
        {
            switch (reader.TokenType)
            {
                case JsonToken.Integer:
                    if (reader.Value is BigInteger big)
                        return big > long.MaxValue ? long.MaxValue : big < long.MinValue ? long.MinValue : (long)big;
                    return Convert.ToInt64(reader.Value);
                case JsonToken.Float:
                    return FromDouble(Convert.ToDouble(reader.Value));
                case JsonToken.Null:
                    return 0;
                default:
                    throw new JsonSerializationException($"Expected a number, got {reader.TokenType}.");
            }
        }

        public override void WriteJson(JsonWriter writer, long value, JsonSerializer serializer) =>
            writer.WriteValue(value);

        private static long FromDouble(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0;

            double rounded = Math.Round(value, MidpointRounding.AwayFromZero);

            // 2⁶³ is exactly representable; casting anything at or above it is undefined, not saturating.
            const double TwoTo63 = 9_223_372_036_854_775_808d;
            if (rounded >= TwoTo63)  return long.MaxValue;
            if (rounded < -TwoTo63)  return long.MinValue;
            return (long)rounded;
        }
    }
}
