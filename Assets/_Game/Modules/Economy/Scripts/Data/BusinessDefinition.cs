using UnityEngine;

namespace KingdomRuler.Modules.Economy
{
    /// <summary>
    /// One business type (GDD §8). One asset per business in <c>ScriptableObjects/Businesses/</c>,
    /// listed on <see cref="EconomyConfig.Businesses"/>.
    /// </summary>
    /// <remarks>
    /// <para>Holds no text: <see cref="BusinessId"/> is its localization key. The key derivation lives
    /// only here, so the Presenter and the content tests cannot disagree about it.</para>
    ///
    /// <para><b>The price is flat.</b> Every unit costs <see cref="UnitPrice"/> however many are
    /// owned, and every unit produces <see cref="GoldPerHour"/>, so <i>n</i> units produce <i>n</i> × it.</para>
    /// </remarks>
    [CreateAssetMenu(fileName = "Business_New", menuName = "Kingdom Ruler/Economy/Business Definition")]
    public sealed class BusinessDefinition : ScriptableObject
    {
        /// <summary>String Table holding every business's name and flavor text.</summary>
        public const string StringTable = "BusinessesTable";

        [Tooltip("Unique id, used in save data and as the localization key: the text lives at " +
                 "\"<BusinessId>.name\" and \"<BusinessId>.flavor\" in BusinessesTable. Renaming it " +
                 "orphans that text and every player's units of this business.")]
        public string BusinessId;

        [Tooltip("Shown on the business's row and in its panel. Empty while art is outstanding; the slot is hidden.")]
        public Sprite Artwork;

        [Tooltip("Whole gold one unit produces per hour. At least 1.")]
        public long GoldPerHour = 1;

        [Tooltip("Whole gold one unit costs — the same for every unit, however many are owned. At least 1.")]
        public long UnitPrice = 1;

        public string NameKey   => BuildNameKey(BusinessId);
        public string FlavorKey => BuildFlavorKey(BusinessId);

        public static string BuildNameKey(string businessId)   => businessId + ".name";
        public static string BuildFlavorKey(string businessId) => businessId + ".flavor";

        private void OnValidate()
        {
            // [Min] does not apply to long fields, so clamp here: a zero rate never fills a storage
            // and a zero price would make the business free.
            if (GoldPerHour < 1) GoldPerHour = 1;
            if (UnitPrice   < 1) UnitPrice   = 1;
        }
    }
}
