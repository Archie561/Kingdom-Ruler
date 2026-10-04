using UnityEngine;

namespace KingdomRuler.Shared.Ledger
{
    /// <summary>
    /// Everything shared that a single trade resource needs in order to be *displayed* —
    /// as opposed to <see cref="TradeResourceState"/>, which is what the Ledger tracks about
    /// it at runtime.
    /// </summary>
    /// <remarks>
    /// <para>Ledger-owned, deliberately, and for the reason <c>ARCHITECTURE.md</c> §4.4 gives
    /// when it names trade resources as the next registry to build: Trade draws these on its
    /// warehouse tiles and offer rows today, Cities needs them for purchase requirements, and
    /// Random Occurrences for outcome text. Naming this after whichever module happened to need
    /// it first is the mistake that rule exists to prevent.</para>
    ///
    /// <para><b>Localization keys are computed, never serialized.</b> A <c>nameKey</c> field
    /// would be one more string to mistype and to drift out of sync with the asset that declares
    /// it. The enum member is already the identity, so the key is derived from it and the two
    /// cannot disagree. Only what genuinely cannot be derived — a sprite reference — is authored
    /// here. Same shape as <see cref="CharacteristicDefinition"/>.</para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "TradeResource",
        menuName = "Kingdom Ruler/Shared/Trade Resource Definition")]
    public sealed class TradeResourceDefinition : ScriptableObject
    {
        /// <summary>
        /// String Table holding every trade resource's display name. Shared rather than
        /// per-module, because Cities and Random Occurrences need the identical strings.
        /// </summary>
        /// <remarks>
        /// Declared here, beside <see cref="NameKey"/>, rather than on the registry: a table
        /// name and the key derivation that addresses it are one fact, and splitting them across
        /// two types makes it unclear which owns the convention.
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

        /// <summary>
        /// String Table entry for this resource's display name — derived from the enum member,
        /// so adding a resource needs no extra wiring here. Resolve it against
        /// <see cref="StringTable"/>.
        /// </summary>
        public string NameKey => BuildNameKey(_type);

        /// <summary>
        /// The name-key convention, in one place. Static so the Editor validator can check the
        /// table against the same derivation the game resolves at runtime, rather than a copy of
        /// it that could quietly disagree.
        /// </summary>
        /// <remarks>
        /// Deliberately terse — <c>resource.stone</c>, matching <c>characteristic.army</c>
        /// rather than a longer <c>traderesource.</c> prefix. The two families already live in
        /// the same table without colliding.
        /// </remarks>
        public static string BuildNameKey(TradeResourceType type) =>
            "resource." + type.ToString().ToLowerInvariant();
    }
}
