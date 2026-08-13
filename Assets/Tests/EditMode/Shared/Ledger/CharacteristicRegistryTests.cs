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
    /// Builds <see cref="CharacteristicRegistry"/> instances the way the Editor authors them —
    /// through SerializedObject, so the tests exercise the same serialized fields the real
    /// assets use rather than a test-only back door.
    /// </summary>
    public static class TestCharacteristics
    {
        public static CharacteristicDefinition Definition(
            CharacteristicType type, Sprite icon, ICollection<UnityEngine.Object> track = null)
        {
            var definition = ScriptableObject.CreateInstance<CharacteristicDefinition>();
            var so = new SerializedObject(definition);
            so.FindProperty("_type").enumValueIndex = (int)type;
            so.FindProperty("_icon").objectReferenceValue = icon;
            so.ApplyModifiedPropertiesWithoutUndo();

            track?.Add(definition);
            return definition;
        }

        public static CharacteristicRegistry Registry(
            IList<CharacteristicDefinition> definitions, ICollection<UnityEngine.Object> track = null)
        {
            var registry = ScriptableObject.CreateInstance<CharacteristicRegistry>();
            var so  = new SerializedObject(registry);
            var arr = so.FindProperty("_definitions");
            arr.arraySize = definitions.Count;
            for (int i = 0; i < definitions.Count; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            track?.Add(registry);
            return registry;
        }

        /// <summary>A complete registry — every characteristic present exactly once.</summary>
        public static CharacteristicRegistry Complete(ICollection<UnityEngine.Object> track = null)
        {
            var definitions = Enum.GetValues(typeof(CharacteristicType))
                                  .Cast<CharacteristicType>()
                                  .Select(t => Definition(t, null, track))
                                  .ToList();
            return Registry(definitions, track);
        }
    }

    [TestFixture]
    public sealed class CharacteristicRegistryTests
    {
        private readonly List<UnityEngine.Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _created)
                if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            _created.Clear();
        }

        [Test]
        public void Get_WithCompleteRegistry_ResolvesEveryCharacteristic()
        {
            var registry = TestCharacteristics.Complete(_created);

            foreach (CharacteristicType type in Enum.GetValues(typeof(CharacteristicType)))
                Assert.That(registry.Get(type), Is.Not.Null,
                    $"{type} did not resolve to a definition.");
        }

        [Test]
        public void NameKey_MatchesTheSharedDerivation_ForEveryCharacteristic()
        {
            var registry = TestCharacteristics.Complete(_created);

            // The registry and the static derivation must agree — the Editor validator checks
            // the String Table against the static one, so a divergence would mean the
            // validator passes on keys the game never asks for.
            foreach (CharacteristicType type in Enum.GetValues(typeof(CharacteristicType)))
                Assert.That(registry.NameKeyFor(type),
                    Is.EqualTo(CharacteristicDefinition.BuildNameKey(type)));
        }

        [Test]
        public void NameKey_IsLowercasedAndPrefixed()
        {
            Assert.That(CharacteristicDefinition.BuildNameKey(CharacteristicType.Infrastructure),
                Is.EqualTo("characteristic.infrastructure"));
        }

        [Test]
        public void FindMissingTypes_WithAGap_NamesTheMissingCharacteristic()
        {
            var partial = Enum.GetValues(typeof(CharacteristicType))
                              .Cast<CharacteristicType>()
                              .Where(t => t != CharacteristicType.Science)
                              .Select(t => TestCharacteristics.Definition(t, null, _created))
                              .ToList();
            var registry = TestCharacteristics.Registry(partial, _created);

            Assert.That(registry.FindMissingTypes(), Is.EquivalentTo(
                new[] { CharacteristicType.Science }));
        }

        [Test]
        public void FindDuplicateTypes_WithTwoAssetsClaimingOneType_ReportsIt()
        {
            var definitions = Enum.GetValues(typeof(CharacteristicType))
                                  .Cast<CharacteristicType>()
                                  .Select(t => TestCharacteristics.Definition(t, null, _created))
                                  .ToList();
            definitions.Add(TestCharacteristics.Definition(CharacteristicType.Army, null, _created));
            var registry = TestCharacteristics.Registry(definitions, _created);

            Assert.That(registry.FindDuplicateTypes(), Is.EquivalentTo(
                new[] { CharacteristicType.Army }));
        }

        [Test]
        public void Get_WithANullEntryInTheArray_SkipsItRatherThanThrowing()
        {
            var definitions = new List<CharacteristicDefinition>
            {
                TestCharacteristics.Definition(CharacteristicType.Army, null, _created),
                null,
                TestCharacteristics.Definition(CharacteristicType.Welfare, null, _created),
            };
            var registry = TestCharacteristics.Registry(definitions, _created);

            Assert.That(registry.Get(CharacteristicType.Army),    Is.Not.Null);
            Assert.That(registry.Get(CharacteristicType.Welfare), Is.Not.Null);
        }

        [Test]
        public void NameKey_WithAnUnregisteredCharacteristic_StillDerivesTheKey()
        {
            // A half-wired registry must not blank out text. The icon goes missing and the
            // validator complains; the label keeps working.
            var registry = TestCharacteristics.Registry(
                new List<CharacteristicDefinition>(), _created);

            Assert.That(registry.NameKeyFor(CharacteristicType.Medicine),
                Is.EqualTo("characteristic.medicine"));
            Assert.That(registry.IconFor(CharacteristicType.Medicine), Is.Null);
        }

        [Test]
        public void IconFor_ReturnsTheAssignedSprite()
        {
            var texture = new Texture2D(4, 4);
            _created.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);
            _created.Add(sprite);

            var definitions = new List<CharacteristicDefinition>
            {
                TestCharacteristics.Definition(CharacteristicType.Army, sprite, _created),
            };
            var registry = TestCharacteristics.Registry(definitions, _created);

            Assert.That(registry.IconFor(CharacteristicType.Army), Is.SameAs(sprite));
        }
    }
}
