using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using UnityEngine;
using KingdomRuler.Modules.Economy;
using KingdomRuler.Modules.Economy.UI;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Tests.EditMode.Systems;

namespace KingdomRuler.Tests.EditMode.Modules.Economy
{
    /// <summary>
    /// Checks the <b>real</b> business assets, config and Economy text in the project, rather than data
    /// a test builds for itself.
    /// </summary>
    /// <remarks>
    /// A wiring mistake fails; unfinished content — an untranslated entry, missing artwork — only
    /// warns (<c>ARCHITECTURE.md</c> §8).
    /// </remarks>
    public sealed class EconomyContentTests
    {
        private static EconomyConfig Config => ProjectAssets.TheOnly<EconomyConfig>();

        /// <summary>A new game starts with the cheapest business's price, so there must be one.</summary>
        [Test]
        public void TheConfig_ListsAtLeastOneBusiness_WithNoEmptySlots()
        {
            var config = Config;

            Assert.IsNotNull(config.Businesses, "EconomyConfig.Businesses is not set.");
            Assert.IsNotEmpty(config.Businesses, "EconomyConfig lists no business, so a new game has no starting price.");
            for (int i = 0; i < config.Businesses.Length; i++)
                Assert.IsNotNull(config.Businesses[i], $"EconomyConfig.Businesses has an empty slot at {i}.");
        }

        /// <summary>
        /// The id is the save key and the localization key: an empty one has no text, and two businesses
        /// sharing one would share their text and the player's units.
        /// </summary>
        [Test]
        public void EveryBusiness_HasAUniqueId()
        {
            var problems     = new List<string>();
            var businessById = new Dictionary<string, BusinessDefinition>();

            foreach (var business in Listed())
            {
                if (string.IsNullOrWhiteSpace(business.BusinessId))
                    problems.Add($"{business.name}: BusinessId is empty, so its text and save data cannot be keyed.");
                else if (!businessById.TryGetValue(business.BusinessId, out var first))
                    businessById.Add(business.BusinessId, business);
                else if (first != business)
                    problems.Add($"{business.name} and {first.name} share the BusinessId '{business.BusinessId}'.");
                else
                    problems.Add($"{business.name} is listed twice.");
            }

            CollectionAssert.IsEmpty(problems, string.Join("\n", problems));
        }

        /// <summary>A business asset the config does not list never appears in the game.</summary>
        [Test]
        public void EveryBusinessAsset_IsListedOnTheConfig()
        {
            var listed   = new HashSet<BusinessDefinition>(Listed());
            var unlisted = new List<string>();
            foreach (var business in ProjectAssets.All<BusinessDefinition>())
                if (!listed.Contains(business)) unlisted.Add(business.name);

            CollectionAssert.IsEmpty(unlisted, "Not on EconomyConfig.Businesses: " + string.Join(", ", unlisted));
        }

        [Test]
        public void EveryBusiness_HasItsTextInEveryLocale()
        {
            // business.NameKey / FlavorKey — the same properties the Presenter resolves.
            var text = new LocalizationCheck(BusinessDefinition.StringTable);
            foreach (var business in Listed())
            {
                if (string.IsNullOrWhiteSpace(business.BusinessId)) continue;   // EveryBusiness_HasAUniqueId
                text.Require(business.NameKey);
                text.Require(business.FlavorKey);
            }
            text.Report();
        }

        [Test]
        public void EveryBusiness_HasAPositiveRateAndPrice()
        {
            var problems = new List<string>();
            foreach (var business in Listed())
            {
                if (business.GoldPerHour < 1) problems.Add($"{business.name}: GoldPerHour {business.GoldPerHour} — its storage never fills.");
                if (business.UnitPrice   < 1) problems.Add($"{business.name}: UnitPrice {business.UnitPrice} — it would be free.");
            }

            CollectionAssert.IsEmpty(problems, string.Join("\n", problems));
        }

        /// <summary>
        /// The Ledger caps gold at <see cref="KingdomLedger.MaxGold"/>. A storage at the units ceiling
        /// above it could hold more than the player may own, and a price above it could never be paid.
        /// </summary>
        [Test]
        public void EveryBusiness_StaysUnderTheGoldCeiling_AtTheUnitsCeiling()
        {
            var config   = Config;
            var problems = new List<string>();
            foreach (var business in Listed())
            {
                // BigInteger: a mistuned rate must fail here, not overflow the check itself.
                BigInteger fullStorage = (BigInteger)business.GoldPerHour * config.StorageHours * EconomyConfig.MaxUnitsPerType;
                if (fullStorage > KingdomLedger.MaxGold)
                    problems.Add($"{business.name}: {EconomyConfig.MaxUnitsPerType:N0} units store {fullStorage:N0} gold, " +
                                 $"above the {KingdomLedger.MaxGold:N0} ceiling.");
                if (business.UnitPrice > KingdomLedger.MaxGold)
                    problems.Add($"{business.name}: price {business.UnitPrice:N0} is above the gold ceiling.");
            }

            CollectionAssert.IsEmpty(problems, string.Join("\n", problems));
        }

        /// <summary>The list order is the display order, and it is cheapest first (GDD §8).</summary>
        [Test]
        public void Businesses_AreListedCheapestFirst()
        {
            var businesses = Listed();
            for (int i = 1; i < businesses.Count; i++)
                Assert.LessOrEqual(businesses[i - 1].UnitPrice, businesses[i].UnitPrice,
                    $"{businesses[i - 1].name} ({businesses[i - 1].UnitPrice:N0}) is listed before the cheaper " +
                    $"{businesses[i].name} ({businesses[i].UnitPrice:N0}).");
        }

        /// <summary>Panel and confirmation text only appears in particular states — easy never to see.</summary>
        [Test]
        public void EveryCodeResolvedMessage_ExistsInEveryLocale()
        {
            var text = new LocalizationCheck(EconomyUIText.StringTable);
            foreach (var key in EconomyUIText.AllKeys) text.Require(key);
            text.Report();
        }

        /// <summary>Missing art is normal mid-project: one grouped warning, never a failure.</summary>
        [Test]
        public void MissingArtwork_IsReportedNotFailed()
        {
            var noArt = new List<string>();
            foreach (var business in Listed())
                if (business.Artwork == null) noArt.Add(business.name);

            if (noArt.Count > 0)
                Debug.LogWarning($"EconomyConfig: {noArt.Count} business artwork(s) missing — " + string.Join(", ", noArt));
        }

        /// <summary>The listed businesses, skipping empty slots (reported by the first test).</summary>
        private static List<BusinessDefinition> Listed()
        {
            var listed = new List<BusinessDefinition>();
            var config = Config;
            if (config.Businesses == null) return listed;

            foreach (var business in config.Businesses)
                if (business != null) listed.Add(business);
            return listed;
        }
    }
}
