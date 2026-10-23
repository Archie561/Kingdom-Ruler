using UnityEngine;

namespace KingdomRuler.Modules.Laws
{
    /// <summary>One law card. One asset per card in <c>ScriptableObjects/LawCards/</c>.</summary>
    /// <remarks>
    /// The card holds no text: <see cref="CardId"/> is its localization key. The key derivation
    /// lives only here, so the Presenter and the content tests cannot disagree about it.
    /// </remarks>
    [CreateAssetMenu(fileName = "LawCard_New", menuName = "Kingdom Ruler/Laws/Law Card Definition")]
    public sealed class LawCardDefinition : ScriptableObject
    {
        /// <summary>String Table holding every card's title and flavor text.</summary>
        public const string StringTable = "LawCardsTable";

        [Tooltip("Unique id, used in save data and as the localization key: the text lives at " +
                 "\"<CardId>.title\" and \"<CardId>.flavor\" in LawCardsTable. Renaming it orphans that text.")]
        public string CardId;

        [Tooltip("Characteristic changes when the player accepts (swipes right).")]
        public LawCardEffect[] AcceptEffects;

        [Tooltip("Characteristic changes when the player rejects (swipes left).")]
        public LawCardEffect[] RejectEffects;

        public string TitleKey  => BuildTitleKey(CardId);
        public string FlavorKey => BuildFlavorKey(CardId);

        public LawCardEffect[] EffectsOf(LawDecision decision) =>
            decision == LawDecision.Accept ? AcceptEffects : RejectEffects;

        public static string BuildTitleKey(string cardId)  => cardId + ".title";
        public static string BuildFlavorKey(string cardId) => cardId + ".flavor";
    }
}
