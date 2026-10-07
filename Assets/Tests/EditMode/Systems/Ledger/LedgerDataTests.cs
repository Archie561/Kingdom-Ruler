using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Tests.EditMode.Systems.Ledger
{
    /// <summary>
    /// Checks the Ledger's <b>real</b> assets: the characteristic and trade-resource registries
    /// and the display names every module shows. <c>CharacteristicRegistryTests</c> and
    /// <c>TradeResourceRegistryTests</c> cover the registry logic on data they build
    /// themselves; this covers what is actually authored.
    /// </summary>
    /// <remarks>
    /// A wiring mistake fails — a type without a definition, two definitions for one type, a
    /// name the game asks for that exists in no locale. Unfinished content only warns — a
    /// missing icon, an untranslated name — because art and translation legitimately lag behind
    /// mid-project (<c>ARCHITECTURE.md</c> §8).
    /// </remarks>
    public sealed class LedgerDataTests
    {
        [Test]
        public void TheCharacteristicRegistry_HasExactlyOneDefinitionPerCharacteristic()
        {
            var registry = ProjectAssets.TheOnly<CharacteristicRegistry>();
            registry.InvalidateIndex();   // the asset may have been edited since it was indexed

            CollectionAssert.IsEmpty(registry.FindMissingTypes(),
                "These characteristics have no definition.");
            CollectionAssert.IsEmpty(registry.FindDuplicateTypes(),
                "These characteristics are claimed by more than one definition; only the first is ever used.");

            var noIcon = new List<string>();
            foreach (CharacteristicType type in Enum.GetValues(typeof(CharacteristicType)))
                if (registry.IconFor(type) == null) noIcon.Add(type.ToString());
            WarnIfAny(noIcon, $"{registry.name}: {noIcon.Count} characteristic icon(s) missing");
        }

        [Test]
        public void TheTradeResourceRegistry_HasExactlyOneDefinitionPerResource()
        {
            var registry = ProjectAssets.TheOnly<TradeResourceRegistry>();
            registry.InvalidateIndex();   // the asset may have been edited since it was indexed

            CollectionAssert.IsEmpty(registry.FindMissingTypes(),
                "These trade resources have no definition.");
            CollectionAssert.IsEmpty(registry.FindDuplicateTypes(),
                "These trade resources are claimed by more than one definition; only the first is ever used.");

            var noIcon = new List<string>();
            foreach (TradeResourceType type in Enum.GetValues(typeof(TradeResourceType)))
                if (registry.IconFor(type) == null) noIcon.Add(type.ToString());
            WarnIfAny(noIcon, $"{registry.name}: {noIcon.Count} trade-resource icon(s) missing");
        }

        [Test]
        public void EveryCharacteristicName_ExistsInEveryLocale()
        {
            // Through the same derivation the game resolves, not a copy of the key pattern — a
            // copy could agree with the table and still disagree with what the game asks for.
            var names = new LocalizationCheck(CharacteristicDefinition.StringTable);
            foreach (CharacteristicType type in Enum.GetValues(typeof(CharacteristicType)))
                names.Require(CharacteristicDefinition.BuildNameKey(type));
            names.Report();
        }

        [Test]
        public void EveryTradeResourceName_ExistsInEveryLocale()
        {
            var names = new LocalizationCheck(TradeResourceDefinition.StringTable);
            foreach (TradeResourceType type in Enum.GetValues(typeof(TradeResourceType)))
                names.Require(TradeResourceDefinition.BuildNameKey(type));
            names.Report();
        }

        private static void WarnIfAny(List<string> items, string summary)
        {
            if (items.Count > 0) Debug.LogWarning(summary + " — " + string.Join(", ", items));
        }
    }
}
