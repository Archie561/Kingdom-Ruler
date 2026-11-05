using System;
using Newtonsoft.Json;
using UnityEngine.Scripting;

namespace KingdomRuler.Systems.Save
{
    /// <summary>
    /// Reads an <c>int</c> field whether the file holds <c>25</c> or <c>25.0</c>, rounding a fraction
    /// to the nearest whole number. Newtonsoft alone rejects <c>25.0</c> for an <c>int</c> and the
    /// whole save would be set aside — this keeps saves that stored the value as a decimal, and
    /// hand-edited ones, readable.
    /// </summary>
    /// <remarks><c>[Preserve]</c>: Newtonsoft creates it by reflection, which IL2CPP stripping cannot see.</remarks>
    [Preserve]
    public sealed class WholeNumberJsonConverter : JsonConverter<int>
    {
        [Preserve]
        public WholeNumberJsonConverter() { }

        public override int ReadJson(JsonReader reader, Type objectType, int existingValue,
                                     bool hasExistingValue, JsonSerializer serializer)
        {
            switch (reader.TokenType)
            {
                case JsonToken.Integer:
                    return Convert.ToInt32(reader.Value);
                case JsonToken.Float:
                    double value = Convert.ToDouble(reader.Value);
                    if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
                    return (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue);
                case JsonToken.Null:
                    return 0;
                default:
                    throw new JsonSerializationException($"Expected a number, got {reader.TokenType}.");
            }
        }

        public override void WriteJson(JsonWriter writer, int value, JsonSerializer serializer) =>
            writer.WriteValue(value);
    }
}
