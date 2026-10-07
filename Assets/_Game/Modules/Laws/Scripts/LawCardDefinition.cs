using UnityEngine;
using KingdomRuler.Modules.Laws.Domain;

namespace KingdomRuler.Modules.Laws
{
    /// <summary>
    /// Data asset for a single law card. One asset per card in ScriptableObjects/LawCards/.
    /// </summary>
    /// <remarks>
    /// Carries no text and no localization-key fields: <see cref="CardId"/> is the key
    /// (<c>ARCHITECTURE.md</c> §2). The derivations below are the only definition of that
    /// convention — the Presenter resolves through them and the content tests check the
    /// table through them, so the two cannot disagree about what the game actually asks for.
    /// This mirrors <c>CharacteristicDefinition</c>.
    /// </remarks>
    [CreateAssetMenu(fileName = "LawCard_New", menuName = "Kingdom Ruler/Laws/Law Card Definition")]
    public sealed class LawCardDefinition : ScriptableObject
    {
        /// <summary>String Table holding every card's title and flavor text.</summary>
        public const string StringTable = "LawCardsTable";

        [Header("Identity")]
        [Tooltip("Unique identifier for this card. Used in save data AND as its localization " +
                 "key: the title and flavor text come from LawCardsTable entries named " +
                 "\"<CardId>.title\" and \"<CardId>.flavor\". Renaming this orphans that text.")]
        public string CardId;

        [Header("Accept Effects")]
        [Tooltip("Characteristic changes when the player swipes right (accepts).")]
        public LawCardEffect[] AcceptEffects;

        [Header("Reject Effects")]
        [Tooltip("Characteristic changes when the player swipes left (rejects).")]
        public LawCardEffect[] RejectEffects;

        /// <summary>String Table entry for this card's title.</summary>
        public string TitleKey => BuildTitleKey(CardId);

        /// <summary>String Table entry for this card's flavor text.</summary>
        public string FlavorKey => BuildFlavorKey(CardId);

        /// <summary>
        /// The title-key convention, in one place. Static so the content tests can check
        /// the table using the same derivation the game resolves with, rather than a copy of
        /// the suffix that could silently drift from it.
        /// </summary>
        public static string BuildTitleKey(string cardId) => cardId + ".title";

        /// <inheritdoc cref="BuildTitleKey"/>
        public static string BuildFlavorKey(string cardId) => cardId + ".flavor";
    }
}
