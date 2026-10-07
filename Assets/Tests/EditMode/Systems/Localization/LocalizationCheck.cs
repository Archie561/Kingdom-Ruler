using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.Localization;
using UnityEngine;

namespace KingdomRuler.Tests.EditMode.Systems
{
    /// <summary>
    /// Checks that the keys the game asks for exist in one real String Table, for the content
    /// tests in every module.
    /// </summary>
    /// <remarks>
    /// <para>Two kinds of problem, treated differently on purpose:</para>
    /// <list type="bullet">
    /// <item><b>An error fails the test</b> — the table does not exist, or a key is in
    /// <i>no</i> locale. The code and the table disagree: a typo, a forgotten entry. That is
    /// never a normal state, and the player would see a <c>[key]</c> placeholder.</item>
    /// <item><b>An untranslated entry is a warning</b> — the key exists but is empty or
    /// missing in one locale. Translations legitimately lag behind mid-project, so this logs
    /// one grouped warning and lets the test pass.</item>
    /// </list>
    /// <para>A warning, not <c>Assert.Warn</c>: Unity's NUnit is 3.5, which predates it. The
    /// Test Framework fails a test only on logged errors and exceptions, so a logged warning
    /// shows in the console and the test output while the test still passes.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var text = new LocalizationCheck(LawCardDefinition.StringTable);
    /// foreach (var card in cards) text.Require(card.TitleKey);
    /// text.Report();
    /// </code>
    /// </example>
    public sealed class LocalizationCheck
    {
        private readonly string _tableName;
        private readonly StringTableCollection _collection;
        private readonly List<string> _errors       = new List<string>();
        private readonly List<string> _untranslated = new List<string>();

        public LocalizationCheck(string tableName)
        {
            _tableName  = tableName;
            _collection = LocalizationEditorSettings.GetStringTableCollection(tableName);
            if (_collection == null) _errors.Add($"String table '{tableName}' does not exist.");
        }

        /// <summary>The game asks for <paramref name="key"/>; check it exists in every locale.</summary>
        public void Require(string key)
        {
            if (_collection == null) return;   // already reported once

            if (!_collection.SharedData.Contains(key))
            {
                _errors.Add($"'{key}' exists in no locale.");
                return;
            }

            foreach (var table in _collection.StringTables)
            {
                var entry = table.GetEntry(key);
                if (entry == null || string.IsNullOrWhiteSpace(entry.Value))
                    _untranslated.Add($"[{table.LocaleIdentifier.Code}] {key}");
            }
        }

        /// <summary>
        /// Log untranslated entries as one grouped warning, then fail if there were any errors,
        /// listing all of them at once.
        /// </summary>
        public void Report()
        {
            if (_untranslated.Count > 0)
                Debug.LogWarning($"{_tableName}: {_untranslated.Count} untranslated — " +
                                 string.Join(", ", _untranslated));

            if (_errors.Count > 0)
                Assert.Fail($"{_tableName}: {_errors.Count} problem(s):\n  • " +
                            string.Join("\n  • ", _errors));
        }
    }
}
