using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KingdomRuler.Tests.EditMode.Systems
{
    /// <summary>
    /// Loads the project's real assets, for the content tests that check what is actually
    /// authored rather than data a test builds for itself.
    /// </summary>
    public static class ProjectAssets
    {
        /// <summary>Every asset of type <typeparamref name="T"/> in the project.</summary>
        public static List<T> All<T>() where T : Object
        {
            var assets = new List<T>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) assets.Add(asset);
            }
            return assets;
        }

        /// <summary>
        /// The one asset of type <typeparamref name="T"/>. Fails if there is none, or more than
        /// one — two would let different modules be wired to different sets.
        /// </summary>
        public static T TheOnly<T>() where T : Object
        {
            var assets = All<T>();
            Assert.AreEqual(1, assets.Count,
                $"There must be exactly one {typeof(T).Name} asset; found {assets.Count}.");
            return assets[0];
        }
    }
}
