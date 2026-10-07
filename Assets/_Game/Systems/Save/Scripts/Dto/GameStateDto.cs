using Newtonsoft.Json;

namespace KingdomRuler.Systems.Save
{
    /// <summary>
    /// Root save data transfer object. Versioned from day one to support
    /// forward-compatible migrations.
    ///
    /// <para><b>Migration policy during development: there is none, by decision.</b>
    /// No real player saves exist yet, so a save that cannot be read is discarded and a
    /// fresh one started (see LocalJsonSaveService.Load). Write the first migration step
    /// when there is something worth preserving — before then it is code maintained
    /// against a case that cannot occur.</para>
    ///
    /// Schema history — kept current so the first real migration has its inputs:
    ///   v1: initial shape.
    ///   v2: removed CharacteristicStateDto.permanentFloor. It was always equal to
    ///       `level`, so nothing is lost — Newtonsoft ignores the now-unknown key.
    ///   v3: renamed the "events" section to "randomOccurrences", and within it
    ///       "pendingEventIds" to "pendingOccurrenceIds" (the mechanic was renamed —
    ///       GDD §10). Older saves deserialize that section as null, so the mailbox
    ///       starts empty.
    ///   v4: laws."lastReplenishCheckUtc" became "nextReplenishDueUtc" — the replenish
    ///       timer now stores the deadline itself rather than the moment it was last
    ///       settled. An older save's key is ignored, so the queue restarts one interval
    ///       from load.
    ///   v5: trade gained "activeOffers" — the generated offers are now persisted, so
    ///       relaunching the app can no longer reroll them for free — and
    ///       trade."lastOfferRefreshUtc" became "nextOfferRefreshDueUtc", storing the
    ///       refresh deadline rather than the moment it was last settled (the same change
    ///       laws made in v4, for the same reason). ledger."tradeResources".*.
    ///       "regenRatePerSecond" was removed: it is always capacity ÷ 86400, and warehouse
    ///       capacity is itself derived from trade."warehouseLevels" on load. A v4 save
    ///       deserializes the new fields as null, so one fresh batch of offers is generated
    ///       and the refresh timer starts one interval from load.
    /// </summary>
    public sealed class GameStateDto
    {
        public const int CurrentSchemaVersion = 5;

        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        // Sub-DTOs will be added here as modules are built:
        [JsonProperty("ledger")]
        public LedgerStateDto Ledger { get; set; }

        [JsonProperty("laws")]
        public LawsStateDto Laws { get; set; }

        [JsonProperty("trade")]
        public TradeStateDto Trade { get; set; }

        [JsonProperty("economy")]
        public EconomyStateDto Economy { get; set; }

        [JsonProperty("cities")]
        public CitiesStateDto Cities { get; set; }

        [JsonProperty("randomOccurrences")]
        public RandomOccurrenceStateDto RandomOccurrences { get; set; }
    }
}
