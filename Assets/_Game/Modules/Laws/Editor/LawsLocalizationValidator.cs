using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;

namespace KingdomRuler.Modules.Laws.Editor
{
    /// <summary>
    /// Checks that every string the Laws screen will ask for actually exists, in every locale.
    /// Laws-owned keys only — characteristic names belong to the Ledger and are checked by
    /// <c>Kingdom Ruler/Validate Shared Data</c>.
    /// </summary>
    /// <remarks>
    /// Localization keys here are <b>derived from data</b> — a card's <c>CardId</c>, a
    /// characteristic's enum name — so the asset side can never drift out of sync. The cost
    /// of that is that a missing or misspelled entry on the *table* side would only show up
    /// as a "[key]" placeholder at runtime, on whichever screen happened to ask for it.
    ///
    /// This closes that gap: it walks the same derivations the Presenter uses and reports
    /// anything absent, so the failure is a menu click rather than a bug report.
    /// </remarks>
    public static class LawsLocalizationValidator
    {
        // The only table name declared here. LawCardsTable is not — that one belongs to
        // LawCardDefinition, along with the key derivation, so this validator checks exactly
        // what the game asks for rather than a matching copy.
        //
        // LawsUITable has no code owner: nothing resolves it in C#, only LocalizeStringEvent
        // components in the prefab. This const is therefore the single code reference, not a
        // duplicate of one.
        private const string LawsUITable = "LawsUITable";

        // No "effect.line": a card deliberately does not tell the player which
        // characteristics it moves before the swipe (GDD §6).
        private static readonly string[] RequiredUIKeys =
            { "ui.waiting", "ui.accept", "ui.reject" };

        [MenuItem("Kingdom Ruler/Validate Laws Localization")]
        public static void Validate()
        {
            var problems = new List<string>();
            int checkedEntries = 0;

            foreach (var key in RequiredUIKeys)
                CheckKey(LawsUITable, key, problems, ref checkedEntries);

            // Card content — two entries per card, named after its id.
            foreach (var config in LoadAll<LawsConfig>())
            {
                if (config.AllCards == null) continue;
                foreach (var card in config.AllCards)
                {
                    if (card == null)
                    {
                        problems.Add($"{config.name}: AllCards contains a null entry.");
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(card.CardId))
                    {
                        problems.Add($"{card.name}: CardId is empty, so its text cannot be keyed.");
                        continue;
                    }
                    // card.TitleKey / card.FlavorKey — the same properties the Presenter
                    // resolves through, not a re-spelling of the suffix.
                    CheckKey(LawCardDefinition.StringTable, card.TitleKey,
                             problems, ref checkedEntries);
                    CheckKey(LawCardDefinition.StringTable, card.FlavorKey,
                             problems, ref checkedEntries);
                }
            }

            Report(problems, checkedEntries);
        }

        private static void CheckKey(string tableName, string key,
                                     ICollection<string> problems, ref int checkedEntries)
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(tableName);
            if (collection == null)
            {
                problems.Add($"Table '{tableName}' does not exist.");
                return;
            }

            foreach (var table in collection.StringTables)
            {
                checkedEntries++;
                var entry = table.GetEntry(key);
                string locale = table.LocaleIdentifier.Code;

                if (entry == null)
                    problems.Add($"{tableName} [{locale}] is missing '{key}'.");
                else if (string.IsNullOrWhiteSpace(entry.Value))
                    problems.Add($"{tableName} [{locale}] has '{key}' but it is empty.");
            }
        }

        private static IEnumerable<T> LoadAll<T>() where T : Object
        {
            foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) yield return asset;
            }
        }

        private static void Report(List<string> problems, int checkedEntries)
        {
            if (problems.Count == 0)
            {
                Debug.Log($"[LawsLocalizationValidator] OK — {checkedEntries} entries present " +
                          "across all locales.");
                return;
            }

            var message = new StringBuilder()
                .AppendLine($"[LawsLocalizationValidator] {problems.Count} problem(s) " +
                            $"in {checkedEntries} checked entries:");
            foreach (var problem in problems) message.Append("  • ").AppendLine(problem);

            // A warning, not an error: untranslated entries are the normal state mid-project,
            // and failing loudly on every domain reload would train people to ignore it.
            Debug.LogWarning(message.ToString());
        }
    }
}
