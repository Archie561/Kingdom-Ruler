using UnityEngine;

namespace KingdomRuler.Shared.Ledger
{
    /// <summary>
    /// Everything shared that a single characteristic needs in order to be *displayed* —
    /// as opposed to <see cref="CharacteristicState"/>, which is what the Ledger tracks
    /// about it at runtime.
    /// </summary>
    /// <remarks>
    /// <para>Ledger-owned, deliberately: Laws draws these on its bars today, Cities needs
    /// them for purchase requirements and Random Occurrences for outcome text
    /// (<c>ARCHITECTURE.md</c> §4.3 — "would a second mechanic need the identical thing?").
    /// Naming this after whichever module happened to need it first is the same mistake as
    /// letting a caller supply the leveling curve.</para>
    ///
    /// <para><b>Localization keys are computed, never serialized.</b> A <c>nameKey</c> field
    /// would be one more string to mistype and to drift out of sync with the asset that
    /// declares it — the exact problem solved by deleting <c>TitleKey</c> from
    /// <c>LawCardDefinition</c>. The enum member is already the identity, so the key is
    /// derived from it and the two cannot disagree. Only things that genuinely cannot be
    /// derived — a sprite reference — are authored here.</para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "Characteristic",
        menuName = "Kingdom Ruler/Shared/Characteristic Definition")]
    public sealed class CharacteristicDefinition : ScriptableObject
    {
        /// <summary>
        /// String Table holding every characteristic's display name. Shared rather than
        /// per-module, because Cities and Random Occurrences need the identical strings.
        /// </summary>
        /// <remarks>
        /// Declared here, beside <see cref="NameKey"/>, rather than on the registry: a table
        /// name and the key derivation that addresses it are one fact, and splitting them
        /// across two types makes it unclear which owns the convention.
        /// </remarks>
        public const string StringTable = "SharedTable";

        [Tooltip("Which of the 6 characteristics this asset describes. Each one must appear " +
                 "exactly once across the registry.")]
        [SerializeField] private CharacteristicType _type;

        [Tooltip("Icon shown beside this characteristic wherever it appears. Optional while " +
                 "art is in progress — a bar with no icon hides the slot rather than drawing " +
                 "an empty box.")]
        [SerializeField] private Sprite _icon;

        /// <summary>Which characteristic this asset describes.</summary>
        public CharacteristicType Type => _type;

        /// <summary>Icon for this characteristic, or null while art is outstanding.</summary>
        public Sprite Icon => _icon;

        /// <summary>
        /// String Table entry for this characteristic's display name — derived from the
        /// enum member, so adding a characteristic needs no extra wiring here.
        /// Resolve it against <see cref="StringTable"/>.
        /// </summary>
        public string NameKey => BuildNameKey(_type);

        /// <summary>
        /// The name-key convention, in one place. Static so the Editor validator can check
        /// the table against the same derivation the game resolves at runtime, rather than
        /// a copy of it that could quietly disagree.
        /// </summary>
        public static string BuildNameKey(CharacteristicType type) =>
            "characteristic." + type.ToString().ToLowerInvariant();
    }
}
