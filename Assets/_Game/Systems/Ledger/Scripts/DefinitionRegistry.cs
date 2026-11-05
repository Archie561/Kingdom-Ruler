using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomRuler.Systems.Ledger
{
    /// <summary>
    /// The one place to look up shared display data for each member of <typeparamref name="TType"/>.
    /// A module that needs an icon or a name key injects the concrete registry rather than deriving
    /// its own.
    /// </summary>
    /// <remarks>
    /// An index over separate definition assets (one asset per entry, <c>CLAUDE.md</c> §7), built
    /// into a dictionary on first use. It owns no localization convention; that lives on the
    /// definition type. A new kind of registered thing is a definition implementing
    /// <see cref="IDefinition{TType}"/> plus a sealed subclass of this.
    /// </remarks>
    public abstract class DefinitionRegistry<TType, TDefinition> : ScriptableObject
        where TType : struct, Enum
        where TDefinition : ScriptableObject, IDefinition<TType>
    {
        [Tooltip("One definition per enum member. Every member must appear exactly once.")]
        [SerializeField] private TDefinition[] _definitions;

        private Dictionary<TType, TDefinition> _byType;

        /// <summary>
        /// The definition for <paramref name="type"/>, or null if the registry is incomplete.
        /// Callers render a fallback rather than assuming non-null: a missing icon must not take
        /// down the screen that asked for it.
        /// </summary>
        public TDefinition Get(TType type)
        {
            EnsureIndexBuilt();
            return _byType.TryGetValue(type, out var definition) ? definition : null;
        }

        /// <summary>Icon for <paramref name="type"/>, or null if unassigned or unregistered.</summary>
        public Sprite IconFor(TType type) => Get(type)?.Icon;

        /// <summary>
        /// Display-name key for <paramref name="type"/>. Falls back to the derivation when the
        /// registry has no entry, so text keeps working while the assets are half-wired.
        /// </summary>
        public string NameKeyFor(TType type) => Get(type)?.NameKey ?? BuildNameKey(type);

        /// <summary>Every member with no usable entry. Empty means the registry is complete.</summary>
        public IEnumerable<TType> FindMissingTypes()
        {
            EnsureIndexBuilt();
            foreach (TType type in Enum.GetValues(typeof(TType)))
                if (!_byType.ContainsKey(type))
                    yield return type;
        }

        /// <summary>
        /// Members claimed by more than one asset. The first entry wins the lookup, so a duplicate is
        /// otherwise invisible until someone edits the shadowed asset and nothing changes on screen.
        /// </summary>
        public IEnumerable<TType> FindDuplicateTypes()
        {
            var seen      = new HashSet<TType>();
            var duplicate = new HashSet<TType>();

            if (_definitions != null)
                foreach (var definition in _definitions)
                    if (definition != null && !seen.Add(definition.Type))
                        duplicate.Add(definition.Type);

            return duplicate;
        }

        /// <summary>Discards the cached lookup so the next call re-reads the array.</summary>
        public void InvalidateIndex() => _byType = null;

        /// <summary>The definition type's own key derivation, for <see cref="NameKeyFor"/>'s fallback.</summary>
        protected abstract string BuildNameKey(TType type);

        // The array was just edited in the Inspector, so any cached lookup is stale.
        protected virtual void OnValidate() => InvalidateIndex();

        private void EnsureIndexBuilt()
        {
            if (_byType != null) return;

            _byType = new Dictionary<TType, TDefinition>();
            if (_definitions == null) return;

            foreach (var definition in _definitions)
            {
                if (definition == null) continue;
                // First one wins; FindDuplicateTypes is what surfaces the shadowed entry.
                if (!_byType.ContainsKey(definition.Type))
                    _byType.Add(definition.Type, definition);
            }
        }
    }
}
