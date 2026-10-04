using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;

namespace KingdomRuler.Modules.Trade.Editor
{
    /// <summary>
    /// Checks that every string the Trade screen needs exists in every locale.
    /// </summary>
    /// <remarks>
    /// <para>Only <b>Trade-owned</b> keys are checked here. Resource names belong to the Ledger
    /// and are covered by <c>Kingdom Ruler/Validate Shared Data</c> — a Trade-owned tool
    /// validating Ledger-owned keys is the arrangement <c>SharedLocalizationValidator</c> exists
    /// to replace.</para>
    ///
    /// <para>Both key lists are imported from <see cref="TradeUIText"/> rather than re-typed.
    /// That is not tidiness: a validator holding its own copy of a key would keep passing while
    /// the game asked for a different string, giving a green menu item and a blank message on
    /// screen — the exact failure this is supposed to catch.</para>
    /// </remarks>
    public static class TradeLocalizationValidator
    {
        [MenuItem("Kingdom Ruler/Validate Trade Localization")]
        public static void Validate()
        {
            var problems = new List<string>();
            int checkedEntries = 0;

            // Keys resolved in code (they take arguments), and the fixed chrome resolved by
            // LocalizeStringEvent components on the prefab. A missing chrome entry would
            // otherwise only surface as a "[ui.x]" placeholder in front of a player.
            CheckKeys(TradeUIText.AllKeys,    problems, ref checkedEntries);
            CheckKeys(TradeUIText.ChromeKeys, problems, ref checkedEntries);

            Report(problems, checkedEntries);
        }

        private static void CheckKeys(IEnumerable<string> keys,
                                      ICollection<string> problems,
                                      ref int checkedEntries)
        {
            var collection = LocalizationEditorSettings
                .GetStringTableCollection(TradeUIText.StringTable);

            if (collection == null)
            {
                problems.Add($"Table '{TradeUIText.StringTable}' does not exist.");
                return;
            }

            foreach (var key in keys)
            {
                foreach (var table in collection.StringTables)
                {
                    checkedEntries++;
                    var entry     = table.GetEntry(key);
                    string locale = table.LocaleIdentifier.Code;

                    if (entry == null)
                        problems.Add($"{TradeUIText.StringTable} [{locale}] is missing '{key}'.");
                    else if (string.IsNullOrWhiteSpace(entry.Value))
                        problems.Add($"{TradeUIText.StringTable} [{locale}] has '{key}' but it is empty.");
                }
            }
        }

        private static void Report(List<string> problems, int checkedEntries)
        {
            if (problems.Count == 0)
            {
                Debug.Log($"[TradeLocalizationValidator] OK — {checkedEntries} entries present " +
                          "across all locales.");
                return;
            }

            var message = new StringBuilder()
                .AppendLine($"[TradeLocalizationValidator] {problems.Count} problem(s):");
            foreach (var problem in problems) message.Append("  • ").AppendLine(problem);

            // A warning, not an error: untranslated entries are the normal state mid-project, and
            // failing loudly on every domain reload trains people to ignore it.
            Debug.LogWarning(message.ToString());
        }
    }
}
