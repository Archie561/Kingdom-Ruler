using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomRuler.Systems.Ledger
{
    /// <summary>
    /// The one place to look up shared display data for a trade resource. Any module that needs
    /// a resource's icon or name key injects this rather than deriving its own.
    /// </summary>
    /// <remarks>
    /// <para>The second instance of the pattern <c>ARCHITECTURE.md</c> §4.4 describes, after
    /// <see cref="CharacteristicRegistry"/> — and the one §4.4 explicitly predicted ("trade
    /// resources are the obvious next one"). Trade is simply its first consumer; Cities will
    /// resolve the identical strings and sprites for purchase costs, and Random Occurrences for
    /// outcome text.</para>
    ///
    /// <para>A registry of separate assets, not one asset holding six inline blocks — per
    /// <c>CLAUDE.md</c> §7: content is one-asset-each, so two people can add or edit different
    /// entries without meeting in the same file.</para>
    ///
    /// <para>The lookup is a dictionary built once on first use. Six linear scans per frame would
    /// not actually hurt anything, but the dictionary is also what makes a duplicate entry
    /// detectable rather than silently shadowed.</para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "TradeResourceRegistry",
        menuName = "Kingdom Ruler/Systems/Trade Resource Registry")]
    public sealed class TradeResourceRegistry : ScriptableObject
    {
        // No StringTable const here: the table name lives on TradeResourceDefinition beside the
        // key derivation that addresses it. This registry is an index over those assets, not the
        // owner of their localization convention.

        [Tooltip("One TradeResourceDefinition per resource. Every value of TradeResourceType " +
                 "must appear exactly once.")]
        [SerializeField] private TradeResourceDefinition[] _definitions;

        private Dictionary<TradeResourceType, TradeResourceDefinition> _byType;

        /// <summary>
        /// The definition for <paramref name="type"/>, or null if the registry is incomplete.
        /// Callers render a sensible fallback rather than assuming non-null: a missing icon must
        /// not take down the screen that asked for it.
        /// </summary>
        public TradeResourceDefinition Get(TradeResourceType type)
        {
            EnsureIndexBuilt();
            return _byType.TryGetValue(type, out var definition) ? definition : null;
        }

        /// <summary>Icon for <paramref name="type"/>, or null if unassigned or unregistered.</summary>
        public Sprite IconFor(TradeResourceType type) => Get(type)?.Icon;

        /// <summary>
        /// String Table key for <paramref name="type"/>'s display name.
        /// </summary>
        /// <remarks>
        /// Falls back to the shared derivation when the registry has no entry, so text keeps
        /// working even while the assets are half-wired — the missing entry then shows up as an
        /// absent icon and a content-test warning, not as a blank label.
        /// </remarks>
        public string NameKeyFor(TradeResourceType type) =>
            Get(type)?.NameKey ?? TradeResourceDefinition.BuildNameKey(type);

        /// <summary>
        /// Every resource that has no usable entry. Empty means the registry is complete.
        /// Used by the Editor validator and by <see cref="OnValidate"/>.
        /// </summary>
        public IEnumerable<TradeResourceType> FindMissingTypes()
        {
            EnsureIndexBuilt();
            foreach (TradeResourceType type in Enum.GetValues(typeof(TradeResourceType)))
                if (!_byType.ContainsKey(type))
                    yield return type;
        }

        /// <summary>
        /// Resources claimed by more than one asset. The first entry wins the lookup, so a
        /// duplicate is otherwise invisible until someone edits the shadowed asset and nothing
        /// changes on screen.
        /// </summary>
        public IEnumerable<TradeResourceType> FindDuplicateTypes()
        {
            var seen      = new HashSet<TradeResourceType>();
            var duplicate = new HashSet<TradeResourceType>();

            if (_definitions != null)
                foreach (var definition in _definitions)
                    if (definition != null && !seen.Add(definition.Type))
                        duplicate.Add(definition.Type);

            return duplicate;
        }

        /// <summary>Discards the cached lookup so the next call re-reads the array.</summary>
        public void InvalidateIndex() => _byType = null;

        private void EnsureIndexBuilt()
        {
            if (_byType != null) return;

            _byType = new Dictionary<TradeResourceType, TradeResourceDefinition>();
            if (_definitions == null) return;

            foreach (var definition in _definitions)
            {
                if (definition == null) continue;
                // First one wins; FindDuplicateTypes is what surfaces the shadowed entry.
                if (!_byType.ContainsKey(definition.Type))
                    _byType.Add(definition.Type, definition);
            }
        }

        private void OnValidate()
        {
            // The array was just edited in the Inspector, so any cached lookup is stale.
            InvalidateIndex();
        }
    }
}
