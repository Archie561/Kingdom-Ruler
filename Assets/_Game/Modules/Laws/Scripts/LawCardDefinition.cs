using UnityEngine;
using KingdomRuler.Modules.Laws.Domain;

namespace KingdomRuler.Modules.Laws
{
    /// <summary>
    /// Data asset for a single law card. One asset per card in ScriptableObjects/Data/.
    /// </summary>
    [CreateAssetMenu(fileName = "LawCard_New", menuName = "Kingdom Ruler/Laws/Law Card Definition")]
    public sealed class LawCardDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Unique identifier for this card (used in save data).")]
        public string CardId;

        [Tooltip("Localization key for the card's flavor text.")]
        public string FlavorTextKey;

        [Header("Accept Effects")]
        [Tooltip("Characteristic changes when the player swipes right (accepts).")]
        public LawCardEffect[] AcceptEffects;

        [Header("Reject Effects")]
        [Tooltip("Characteristic changes when the player swipes left (rejects).")]
        public LawCardEffect[] RejectEffects;
    }
}
