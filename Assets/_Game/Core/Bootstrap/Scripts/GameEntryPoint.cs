using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;
using KingdomRuler.Systems.Localization;

namespace KingdomRuler.Core
{
    /// <summary>
    /// Startup, in an order that is load-bearing (<c>ARCHITECTURE.md</c> §4.1):
    ///   1. Hydrate every system from the save (or start a fresh game).
    ///   2. Wait for localization to finish loading.
    ///   3. Load the Main scene additively.
    ///   4. Inject the Views it brought with it.
    /// </summary>
    /// <remarks>
    /// <para>Step 1 before step 3: Views render whatever state they find on <c>Start</c>.</para>
    ///
    /// <para>Step 2 before step 3: a scene loaded earlier keeps its prefab text and its
    /// <c>[key]</c> placeholders <b>permanently</b> — neither localization path recovers. It is not
    /// a first-frame flash.</para>
    /// </remarks>
    public sealed class GameEntryPoint : IStartable, IDisposable
    {
        private const string MainSceneName = "Main";

        private readonly GameStateCoordinator _stateCoordinator;
        private readonly ILocalizationService _localization;
        private readonly IObjectResolver      _resolver;

        public GameEntryPoint(GameStateCoordinator stateCoordinator,
                              ILocalizationService localization,
                              IObjectResolver      resolver)
        {
            _stateCoordinator = stateCoordinator;
            _localization     = localization;
            _resolver         = resolver;
        }

        public void Start()
        {
            _stateCoordinator.LoadOrInitialize();

            // Synchronous if localization is already up.
            _localization.WhenReady(LoadMainScene);
        }

        private void LoadMainScene()
        {
            // Subscribe before loading: a non-async LoadScene still completes at the end
            // of the frame, so the roots are not queryable at the point the call returns.
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.LoadScene(MainSceneName, LoadSceneMode.Additive);
        }

        public void Dispose() => SceneManager.sceneLoaded -= OnSceneLoaded;

        /// <summary>
        /// Inject the freshly-loaded scene's Views.
        /// </summary>
        /// <remarks>
        /// VContainer only wires MonoBehaviours in its own scope's scene, and the root scope lives
        /// in Bootstrap, so an additively loaded View would never be injected. Injecting the roots
        /// here keeps the single root scope (<c>ARCHITECTURE.md</c> §1.6); <c>InjectGameObject</c>
        /// walks children, so one call per root covers the tree.
        /// </remarks>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != MainSceneName) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;

            foreach (var root in scene.GetRootGameObjects())
            {
                try
                {
                    _resolver.InjectGameObject(root);
                }
                catch (Exception ex)
                {
                    // One unresolvable View must not stop the rest of the screen wiring up,
                    // and the message needs to name the object or it is untraceable.
                    Debug.LogError(
                        $"[GameEntryPoint] Failed to inject '{root.name}' in scene " +
                        $"'{scene.name}': {ex.GetType().Name}: {ex.Message}", root);
                }
            }
        }
    }
}
