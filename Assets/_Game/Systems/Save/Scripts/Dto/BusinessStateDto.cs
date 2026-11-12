using Newtonsoft.Json;

namespace KingdomRuler.Systems.Save
{
    /// <summary>
    /// One business type's persisted state. Nested under <see cref="EconomyStateDto"/>, keyed by BusinessId.
    /// </summary>
    /// <remarks>
    /// <para>Capacity and rate are <b>not</b> saved: they derive from the units owned through the
    /// current content, so a retuned rate reaches existing saves (as Trade's levels do).</para>
    ///
    /// <para>Whole numbers, read through the whole-number converters so a file holding <c>12.0</c>
    /// still loads instead of being set aside as corrupt.</para>
    /// </remarks>
    public sealed class BusinessStateDto
    {
        [JsonProperty("unitsOwned")]
        [JsonConverter(typeof(WholeNumberJsonConverter))]
        public int UnitsOwned { get; set; }

        /// <summary>Gold in the storage, not yet collected.</summary>
        [JsonProperty("stored")]
        [JsonConverter(typeof(WholeNumberLongJsonConverter))]
        public long Stored { get; set; }

        /// <summary>ISO-8601 UTC: when the coin now accruing started. Drives offline accrual on load.</summary>
        [JsonProperty("lastUpdatedUtc")]
        public string LastUpdatedUtc { get; set; }
    }
}
