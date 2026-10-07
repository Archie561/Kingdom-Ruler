using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomRuler.Systems.Popups
{
    /// <summary>
    /// Every popup prefab in the game — shared ones and module-owned ones alike — one per kind.
    /// </summary>
    /// <remarks>
    /// An asset rather than a list on <see cref="PopupSystem"/>, so adding a popup edits this
    /// file instead of the Bootstrap scene. The field is typed <see cref="Popup"/>, so the
    /// Inspector only accepts prefabs that have one on their root.
    /// </remarks>
    [CreateAssetMenu(fileName = "PopupRegistry", menuName = "Kingdom Ruler/Systems/Popup Registry")]
    public sealed class PopupRegistry : ScriptableObject
    {
        [Tooltip("One prefab per kind of popup.")]
        [SerializeField] private Popup[] _prefabs = Array.Empty<Popup>();

        /// <summary>The prefab for popups of type <typeparamref name="T"/>.</summary>
        /// <exception cref="InvalidOperationException">None is registered.</exception>
        public T Find<T>() where T : Popup
        {
            foreach (var prefab in _prefabs)
                if (prefab != null && prefab is T match) return match;

            throw new InvalidOperationException(
                $"No {typeof(T).Name} prefab in the popup registry '{name}'. Add one there.");
        }

        // Two prefabs of one kind would leave the second silently unused.
        private void OnValidate()
        {
            var seen = new HashSet<Type>();
            foreach (var prefab in _prefabs)
                if (prefab != null && !seen.Add(prefab.GetType()))
                    Debug.LogWarning(
                        $"[PopupRegistry] Two prefabs are a {prefab.GetType().Name}; only the first " +
                        "will ever open.", this);
        }
    }
}
