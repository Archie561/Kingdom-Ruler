using UnityEngine;

namespace KingdomRuler.Systems.Ledger
{
    /// <summary>
    /// What a characteristic needs to be <i>displayed</i>, as opposed to
    /// <see cref="CharacteristicState"/>, which the Ledger tracks at runtime.
    /// </summary>
    /// <remarks>
    /// <para>Ledger-owned because several mechanics display characteristics
    /// (<c>ARCHITECTURE.md</c> §4.3, "Shared display data").</para>
    ///
    /// <para><b>The localization key is derived from the enum member, never serialized</b>, so it
    /// cannot be mistyped or drift from the asset. Only what cannot be derived — the sprite — is
    /// authored.</para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "Characteristic",
        menuName = "Kingdom Ruler/Systems/Characteristic Definition")]
    public sealed class CharacteristicDefinition : ScriptableObject, IDefinition<CharacteristicType>
    {
        /// <summary>String Table holding every characteristic's display name.</summary>
        /// <remarks>
        /// Declared beside <see cref="NameKey"/>, not on the registry: a table name and the key
        /// derivation that addresses it are one fact.
        /// </remarks>
        public const string StringTable = "SharedTable";

        [Tooltip("Which of the 6 characteristics this asset describes. Each one must appear " +
                 "exactly once across the registry.")]
        [SerializeField] private CharacteristicType _type;

        [Tooltip("Icon shown wherever this characteristic appears. Optional while art is in " +
                 "progress — a view with no icon hides the slot rather than drawing an empty box.")]
        [SerializeField] private Sprite _icon;

        /// <summary>Which characteristic this asset describes.</summary>
        public CharacteristicType Type => _type;

        /// <summary>Icon for this characteristic, or null while art is outstanding.</summary>
        public Sprite Icon => _icon;

        /// <summary>This characteristic's display-name entry in <see cref="StringTable"/>.</summary>
        public string NameKey => BuildNameKey(_type);

        /// <summary>
        /// The name-key convention, in one place. Static so the content tests check the table
        /// through the same derivation the game resolves with.
        /// </summary>
        public static string BuildNameKey(CharacteristicType type) =>
            "characteristic." + type.ToString().ToLowerInvariant();
    }
}
