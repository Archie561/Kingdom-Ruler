using UnityEngine;

namespace KingdomRuler.Systems.Ledger
{
    /// <summary>
    /// What a trade resource needs to be <i>displayed</i>, as opposed to
    /// <see cref="TradeResourceState"/>, which the Ledger tracks at runtime. Same shape as
    /// <see cref="CharacteristicDefinition"/>.
    /// </summary>
    /// <remarks>
    /// <para>Ledger-owned because several mechanics display trade resources
    /// (<c>ARCHITECTURE.md</c> §4.3, "Shared display data").</para>
    ///
    /// <para><b>The localization key is derived from the enum member, never serialized</b>, so it
    /// cannot be mistyped or drift from the asset. Only the sprite is authored.</para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "TradeResource",
        menuName = "Kingdom Ruler/Systems/Trade Resource Definition")]
    public sealed class TradeResourceDefinition : ScriptableObject, IDefinition<TradeResourceType>
    {
        /// <summary>String Table holding every trade resource's display name.</summary>
        /// <remarks>
        /// Declared beside <see cref="NameKey"/>, not on the registry: a table name and the key
        /// derivation that addresses it are one fact.
        /// </remarks>
        public const string StringTable = "SharedTable";

        [Tooltip("Which of the 6 trade resources this asset describes. Each one must appear " +
                 "exactly once across the registry.")]
        [SerializeField] private TradeResourceType _type;

        [Tooltip("Icon shown beside this resource wherever it appears — warehouse tiles, offer " +
                 "rows, city purchase costs. Optional while art is in progress; a view hides the " +
                 "slot rather than drawing an empty box.")]
        [SerializeField] private Sprite _icon;

        /// <summary>Which trade resource this asset describes.</summary>
        public TradeResourceType Type => _type;

        /// <summary>Icon for this resource, or null while art is outstanding.</summary>
        public Sprite Icon => _icon;

        /// <summary>This resource's display-name entry in <see cref="StringTable"/>.</summary>
        public string NameKey => BuildNameKey(_type);

        /// <summary>
        /// The name-key convention (<c>resource.stone</c>), in one place. Static so the content
        /// tests check the table through the same derivation the game resolves with.
        /// </summary>
        public static string BuildNameKey(TradeResourceType type) =>
            "resource." + type.ToString().ToLowerInvariant();
    }
}
