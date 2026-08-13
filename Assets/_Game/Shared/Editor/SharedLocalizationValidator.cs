using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using KingdomRuler.Shared.Ledger;

namespace KingdomRuler.Shared.Editor
{
    /// <summary>
    /// Checks the strings and shared data every module depends on: the characteristic
    /// registry, and the characteristic names in <see cref="CharacteristicDefinition.StringTable"/>.
    /// </summary>
    /// <remarks>
    /// This lives in Shared rather than inside a module because characteristics are
    /// Ledger-owned. It used to sit in the Laws validator, which meant a Laws-owned tool was
    /// the only thing checking keys Laws does not own — the moment Cities arrived it would
    /// either duplicate the loop or leave the keys unchecked, and running the Laws menu item
    /// would have been the only way to notice a broken shared string.
    /// </remarks>
    public static class SharedLocalizationValidator
    {
        [MenuItem("Kingdom Ruler/Validate Shared Data")]
        public static void Validate()
        {
            var problems = new List<string>();
            int checkedEntries = 0;

            ValidateRegistry(problems);
            ValidateCharacteristicNames(problems, ref checkedEntries);

            Report(problems, checkedEntries);
        }

        /// <summary>
        /// Every characteristic resolves to exactly one definition, and every definition has
        /// art. A gap here is invisible at runtime — the name still resolves, because
        /// <see cref="CharacteristicRegistry.NameKeyFor"/> falls back to the derivation, and
        /// only the icon quietly goes missing.
        /// </summary>
        private static void ValidateRegistry(ICollection<string> problems)
        {
            var registries = LoadAll<CharacteristicRegistry>();
            if (registries.Count == 0)
            {
                problems.Add("No CharacteristicRegistry asset exists.");
                return;
            }

            if (registries.Count > 1)
                problems.Add($"{registries.Count} CharacteristicRegistry assets exist — there " +
                             "should be exactly one, or modules can be wired to different sets.");

            foreach (var registry in registries)
            {
                // The array may have been edited since this registry was last loaded.
                registry.InvalidateIndex();

                foreach (var missing in registry.FindMissingTypes())
                    problems.Add($"{registry.name}: no definition for '{missing}'.");

                foreach (var duplicate in registry.FindDuplicateTypes())
                    problems.Add($"{registry.name}: '{duplicate}' is claimed by more than one " +
                                 "definition; only the first is ever used.");

                foreach (CharacteristicType type in System.Enum.GetValues(typeof(CharacteristicType)))
                {
                    var definition = registry.Get(type);
                    if (definition != null && definition.Icon == null)
                        problems.Add($"{definition.name}: no icon assigned.");
                }
            }
        }

        private static void ValidateCharacteristicNames(ICollection<string> problems,
                                                        ref int checkedEntries)
        {
            var collection = LocalizationEditorSettings
                .GetStringTableCollection(CharacteristicDefinition.StringTable);

            if (collection == null)
            {
                problems.Add($"Table '{CharacteristicDefinition.StringTable}' does not exist.");
                return;
            }

            foreach (CharacteristicType type in System.Enum.GetValues(typeof(CharacteristicType)))
            {
                // Deliberately the same derivation the game resolves through, not a copy of
                // the string pattern — a copy could agree with the table and still disagree
                // with what actually gets asked for at runtime.
                string key = CharacteristicDefinition.BuildNameKey(type);

                foreach (var table in collection.StringTables)
                {
                    checkedEntries++;
                    var entry  = table.GetEntry(key);
                    string locale = table.LocaleIdentifier.Code;

                    if (entry == null)
                        problems.Add($"{CharacteristicDefinition.StringTable} [{locale}] is " +
                                     $"missing '{key}'.");
                    else if (string.IsNullOrWhiteSpace(entry.Value))
                        problems.Add($"{CharacteristicDefinition.StringTable} [{locale}] has " +
                                     $"'{key}' but it is empty.");
                }
            }
        }

        private static List<T> LoadAll<T>() where T : Object
        {
            var results = new List<T>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) results.Add(asset);
            }
            return results;
        }

        private static void Report(List<string> problems, int checkedEntries)
        {
            if (problems.Count == 0)
            {
                Debug.Log($"[SharedLocalizationValidator] OK — registry complete, " +
                          $"{checkedEntries} name entries present across all locales.");
                return;
            }

            var message = new StringBuilder()
                .AppendLine($"[SharedLocalizationValidator] {problems.Count} problem(s):");
            foreach (var problem in problems) message.Append("  • ").AppendLine(problem);

            // A warning, not an error: missing art and untranslated entries are the normal
            // state mid-project, and failing loudly on every domain reload trains people to
            // ignore it.
            Debug.LogWarning(message.ToString());
        }
    }
}
