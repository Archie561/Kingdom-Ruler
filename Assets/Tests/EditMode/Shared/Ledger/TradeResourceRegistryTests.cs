using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using KingdomRuler.Shared.Ledger;

namespace KingdomRuler.Tests.EditMode.Shared.Ledger
{
    /// <summary>
    /// Builds <see cref="TradeResourceRegistry"/> instances the way the Editor authors them —
    /// through SerializedObject, so the tests exercise the same serialized fields the real assets
    /// use rather than a test-only back door. Mirror of <see cref="TestCharacteristics"/>.
    /// </summary>
    public static class TestTradeResources
    {
        public static TradeResourceDefinition Definition(
            TradeResourceType type, Sprite icon, ICollection<UnityEngine.Object> track = null)
        {
            var definition = ScriptableObject.CreateInstance<TradeResourceDefinition>();
            var so = new SerializedObject(definition);
            so.FindProperty("_type").enumValueIndex = (int)type;
            so.FindProperty("_icon").objectReferenceValue = icon;
            so.ApplyModifiedPropertiesWithoutUndo();

            track?.Add(definition);
            return definition;
        }

        public static TradeResourceRegistry Registry(
            IList<TradeResourceDefinition> definitions, ICollection<UnityEngine.Object> track = null)
        {
            var registry = ScriptableObject.CreateInstance<TradeResourceRegistry>();
            var so  = new SerializedObject(registry);
            var arr = so.FindProperty("_definitions");
            arr.arraySize = definitions.Count;
            for (int i = 0; i < definitions.Count; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            track?.Add(registry);
            return registry;
        }

        /// <summary>A complete registry — every trade resource present exactly once.</summary>
        public static TradeResourceRegistry Complete(ICollection<UnityEngine.Object> track = null)
        {
            var definitions = Enum.GetValues(typeof(TradeResourceType))
                                  .Cast<TradeResourceType>()
                                  .Select(t => Definition(t, null, track))
                                  .ToList();
            return Registry(definitions, track);
        }
    }

    [TestFixture]
    public sealed class TradeResourceRegistryTests
    {
        private readonly List<UnityEngine.Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _created)
                if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            _created.Clear();
        }

        // ── Lookup ────────────────────────────────────────────────────────────────

        [Test]
        public void Get_WithCompleteRegistry_ResolvesEveryResource()
        {
            var registry = TestTradeResources.Complete(_created);

            foreach (TradeResourceType type in Enum.GetValues(typeof(TradeResourceType)))
                Assert.IsNotNull(registry.Get(type), $"No definition resolved for {type}.");
        }

        [Test]
        public void Get_WithMissingEntry_ReturnsNullRatherThanThrowing()
        {
            // A half-wired registry must degrade, not take down the screen that asked.
            var partial = TestTradeResources.Registry(
                new List<TradeResourceDefinition>
                {
                    TestTradeResources.Definition(TradeResourceType.Stone, null, _created)
                }, _created);

            Assert.IsNotNull(partial.Get(TradeResourceType.Stone));
            Assert.IsNull(partial.Get(TradeResourceType.Clay));
        }

        [Test]
        public void Registry_IgnoresNullEntries()
        {
            var registry = TestTradeResources.Registry(
                new List<TradeResourceDefinition>
                {
                    null,
                    TestTradeResources.Definition(TradeResourceType.Wood, null, _created),
                    null
                }, _created);

            Assert.IsNotNull(registry.Get(TradeResourceType.Wood));
            Assert.DoesNotThrow(() => registry.FindMissingTypes().ToList());
        }

        // ── Completeness reporting ────────────────────────────────────────────────

        [Test]
        public void FindMissingTypes_IsEmptyForACompleteRegistry()
        {
            var registry = TestTradeResources.Complete(_created);

            CollectionAssert.IsEmpty(registry.FindMissingTypes().ToList());
        }

        [Test]
        public void FindMissingTypes_NamesEveryAbsentResource()
        {
            var registry = TestTradeResources.Registry(
                new List<TradeResourceDefinition>
                {
                    TestTradeResources.Definition(TradeResourceType.Stone, null, _created),
                    TestTradeResources.Definition(TradeResourceType.Wood,  null, _created)
                }, _created);

            var missing = registry.FindMissingTypes().ToList();

            Assert.AreEqual(4, missing.Count);
            CollectionAssert.DoesNotContain(missing, TradeResourceType.Stone);
            CollectionAssert.Contains(missing, TradeResourceType.Clay);
        }

        [Test]
        public void FindDuplicateTypes_SurfacesAShadowedEntry()
        {
            // The first entry wins the lookup, so without this the second asset is edited and
            // nothing changes on screen — invisible without the check.
            var registry = TestTradeResources.Registry(
                new List<TradeResourceDefinition>
                {
                    TestTradeResources.Definition(TradeResourceType.Metal, null, _created),
                    TestTradeResources.Definition(TradeResourceType.Metal, null, _created)
                }, _created);

            CollectionAssert.Contains(registry.FindDuplicateTypes().ToList(), TradeResourceType.Metal);
        }

        // ── Localization keys ─────────────────────────────────────────────────────

        [Test]
        public void NameKeyFor_FallsBackToTheDerivation_WhenTheEntryIsMissing()
        {
            // Text keeps working while art is outstanding; only the icon goes absent.
            var empty = TestTradeResources.Registry(new List<TradeResourceDefinition>(), _created);

            Assert.AreEqual(TradeResourceDefinition.BuildNameKey(TradeResourceType.Leather),
                            empty.NameKeyFor(TradeResourceType.Leather));
        }

        [Test]
        public void RegistryKey_AgreesWithTheStaticDerivation()
        {
            // The property the game resolves through and the static the validator checks through
            // must be the same string, or the menu item goes green while the label goes blank.
            var registry = TestTradeResources.Complete(_created);

            foreach (TradeResourceType type in Enum.GetValues(typeof(TradeResourceType)))
                Assert.AreEqual(TradeResourceDefinition.BuildNameKey(type),
                                registry.NameKeyFor(type), $"Key disagreement for {type}.");
        }

        [Test]
        public void EveryResource_DerivesADistinctKey()
        {
            var keys = new HashSet<string>();

            foreach (TradeResourceType type in Enum.GetValues(typeof(TradeResourceType)))
            {
                string key = TradeResourceDefinition.BuildNameKey(type);
                Assert.IsTrue(keys.Add(key), $"'{key}' is derived by more than one resource.");
            }
        }

        [Test]
        public void LabelKeys_FollowTheDocumentedConvention()
        {
            // Terse "resource.", matching "characteristic.army" — both families share SharedTable.
            Assert.AreEqual("resource.stone", TradeResourceDefinition.BuildNameKey(TradeResourceType.Stone));
            Assert.AreEqual("resource.clay",  TradeResourceDefinition.BuildNameKey(TradeResourceType.Clay));
        }

        [Test]
        public void TradeResourceKeys_DoNotCollideWithCharacteristicKeys()
        {
            // Both live in SharedTable, so a prefix collision would silently overwrite entries.
            var resourceKeys = Enum.GetValues(typeof(TradeResourceType)).Cast<TradeResourceType>()
                                   .Select(TradeResourceDefinition.BuildNameKey);
            var characteristicKeys = Enum.GetValues(typeof(CharacteristicType)).Cast<CharacteristicType>()
                                   .Select(CharacteristicDefinition.BuildNameKey)
                                   .ToHashSet();

            Assert.AreEqual(TradeResourceDefinition.StringTable, CharacteristicDefinition.StringTable,
                "This test only matters while both families share one table.");

            foreach (var key in resourceKeys)
                Assert.IsFalse(characteristicKeys.Contains(key), $"'{key}' collides.");
        }

        // ── Index caching ─────────────────────────────────────────────────────────

        [Test]
        public void InvalidateIndex_RereadsTheArray()
        {
            var registry = TestTradeResources.Registry(new List<TradeResourceDefinition>(), _created);
            Assert.IsNull(registry.Get(TradeResourceType.Stone));   // builds and caches an empty index

            var so  = new SerializedObject(registry);
            var arr = so.FindProperty("_definitions");
            arr.arraySize = 1;
            arr.GetArrayElementAtIndex(0).objectReferenceValue =
                TestTradeResources.Definition(TradeResourceType.Stone, null, _created);
            so.ApplyModifiedPropertiesWithoutUndo();

            registry.InvalidateIndex();

            Assert.IsNotNull(registry.Get(TradeResourceType.Stone));
        }
    }
}
