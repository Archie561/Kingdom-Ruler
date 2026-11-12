using UnityEngine;

namespace KingdomRuler.Modules.Economy
{
    /// <summary>
    /// Tunable parameters for the Economy mechanic (<c>GDD.md</c> §8), and the list of business types.
    /// One asset in <c>ScriptableObjects/Config/</c>.
    /// </summary>
    /// <remarks>
    /// <para>The businesses themselves are separate assets in <c>ScriptableObjects/Businesses/</c>; this
    /// only lists them, as <c>LawsConfig.AllCards</c> lists the cards. <b>The list order is the display
    /// order</b>, cheapest first — <c>EconomyContentTests</c> checks it rises by price.</para>
    ///
    /// <para><b>Module-owned, not Ledger-owned.</b> Only Economy has storages, so nothing else has to
    /// agree on these numbers (<c>ARCHITECTURE.md</c> §4.3). The fill math itself is shared:
    /// <c>Shared/Accrual/WholeUnitAccrual</c>, with <see cref="StorageHours"/> as its period.</para>
    /// </remarks>
    [CreateAssetMenu(fileName = "EconomyConfig", menuName = "Kingdom Ruler/Economy/Economy Config")]
    public sealed class EconomyConfig : ScriptableObject
    {
        /// <summary>
        /// The most units of one business type a player can own — far beyond play. A safety ceiling,
        /// not a tunable: with the authored rates and <see cref="StorageHours"/> it keeps every storage
        /// under <c>KingdomLedger.MaxGold</c>, which <c>EconomyContentTests</c> checks for each business.
        /// </summary>
        public const int MaxUnitsPerType = 1_000_000;

        [Header("Storage")]
        [Tooltip("Hours of production one storage holds. An empty storage is full after exactly this long, " +
                 "at any number of units. GDD §8 says 24.")]
        [Min(1)]
        public int StorageHours = 24;

        [Header("Crystal top-up")]
        [Tooltip("Gold one crystal covers when the player is short. Price = ceil(missing ÷ this), at least 1 crystal (GDD §8).")]
        [Min(1)]
        public int GoldPerCrystal = 100;

        [Header("Content")]
        [Tooltip("Every business type, in display order, cheapest first. Each stays a separate asset in " +
                 "ScriptableObjects/Businesses/. A new game starts with exactly the cheapest one's price.")]
        public BusinessDefinition[] Businesses;

        private void OnValidate()
        {
            if (StorageHours   < 1) StorageHours   = 1;
            if (GoldPerCrystal < 1) GoldPerCrystal = 1;
        }
    }
}
