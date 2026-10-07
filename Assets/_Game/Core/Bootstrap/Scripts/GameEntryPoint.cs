using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;
using KingdomRuler.Systems.Localization;

namespace KingdomRuler.Core
{
    /// <summary>
    /// Root startup entry point. Registered as IStartable in the Bootstrap scope.
    /// Responsibilities, in order:
    ///   1. Hydrate every system from the save file (or start a fresh game).
    ///   2. Wait for localization to finish loading.
    ///   3. Load the Main scene additively.
    ///   4. Inject the Views it brought with it.
    /// </summary>
    /// <remarks>
    /// <para>Step 1 must finish before step 3: the Views render whatever state they find on
    /// Start, and a module initialised after its View has already rendered would show a
    /// blank screen until the next change. The dependency on GameStateCoordinator is
    /// explicit rather than relying on entry-point registration order.</para>
    ///
    /// <para>Step 2 must also finish before step 3, and this was verified by removing it:
    /// with the scene loaded before localization finished, the ACCEPT/REJECT chrome kept the
    /// English baked into the prefab and every Presenter-built string stayed on its
    /// "[key]" placeholder — permanently, long after initialisation had completed.
    /// <b>Neither path recovers on its own</b>, so do not assume the placeholders are a
    /// transient first-frame flash that will resolve itself. Loading the scene into an
    /// already-initialised localization system is what makes the text correct, and it costs
    /// a few frames at boot, once.</para>
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

            // Runs synchronously if localization is already up, so this is not automatically
            // a deferred path — see the class remarks for why the wait exists at all.
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
        /// The root LifetimeScope lives in the Bootstrap scene, and VContainer only wires
        /// MonoBehaviours a scope can see in its own scene. Anything in an additively
        /// loaded scene is therefore invisible to it: a View would reach Start with its
        /// [Inject] method never called and null-ref on the first line.
        ///
        /// Injecting the roots here — rather than giving Main its own child scope — keeps
        /// the single-root-scope rule in ARCHITECTURE.md §1.6 intact.
        /// <c>InjectGameObject</c> walks children, so one call per root covers the tree.
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
