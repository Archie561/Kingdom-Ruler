using UnityEngine;
using UnityEngine.Serialization;
using KingdomRuler.Shared.Ledger;
using KingdomRuler.Shared.Services;
using KingdomRuler.Modules.Laws;
using KingdomRuler.Modules.Laws.Presenters;
using VContainer;
using VContainer.Unity;
using KingdomRuler.Modules.Trade;
using KingdomRuler.Modules.Economy;
using KingdomRuler.Modules.Cities;
using KingdomRuler.Modules.RandomOccurrences;

namespace KingdomRuler.Core
{
    public sealed class GameBootstrapper : LifetimeScope
    {
        [Header("Shared")]
        [Tooltip("Ledger-owned characteristic leveling curve. Shared by every mechanic " +
                 "that awards characteristic points — see ARCHITECTURE.md §4.3.")]
        [SerializeField] private LevelingConfig _levelingConfig;

        [Header("Modules")]
        [SerializeField] private LawsConfig    _lawsConfig;
        [SerializeField] private TradeConfig   _tradeConfig;
        [SerializeField] private EconomyConfig _economyConfig;

        // FormerlySerializedAs carries the value across the Events → RandomOccurrences
        // rename; the scene stores the field by name, so without it Unity would silently
        // drop the reference and the config would arrive null at runtime.
        [FormerlySerializedAs("_eventsConfig")]
        [SerializeField] private RandomOccurrenceConfig _randomOccurrenceConfig;

        protected override void Configure(IContainerBuilder builder)
        {
            // Core
            builder.Register<EventBus>(Lifetime.Singleton);

            // The Ledger is built by hand so it receives the leveling curve as a plain
            // value type — that keeps KingdomLedger free of ScriptableObject/UnityEngine
            // dependencies and directly unit-testable.
            builder.Register(c => new KingdomLedger(c.Resolve<EventBus>(), ResolveLevelingCurve()),
                Lifetime.Singleton);

            // Laws module.
            // Explicit factory: LawsManager has a second constructor taking a System.Random
            // (so tests can pin the shuffle order). VContainer picks the constructor with
            // the most parameters, so left to itself it selects that one and fails to
            // resolve System.Random at startup.
            builder.Register(c => new LawsManager(
                    c.Resolve<KingdomLedger>(), c.Resolve<IClock>(), c.Resolve<LawsConfig>()),
                Lifetime.Singleton);
            builder.Register<LawsPresenter>(Lifetime.Singleton);
            builder.RegisterInstance(_lawsConfig);
            // Advances the card-replenishment timer independently of any View.
            builder.RegisterEntryPoint<LawsTickDriver>();

            // Trade module
            builder.Register<TradeManager>(Lifetime.Singleton);
            builder.RegisterInstance(_tradeConfig);

            // Economy module
            builder.Register<EconomyManager>(Lifetime.Singleton);
            builder.RegisterInstance(_economyConfig);

            // Cities module
            builder.Register<CitiesManager>(Lifetime.Singleton);

            // Random Occurrences module (GDD §10)
            builder.Register<RandomOccurrenceManager>(Lifetime.Singleton);
            builder.RegisterInstance(_randomOccurrenceConfig);

            // Services
            builder.Register<SystemClock>(Lifetime.Singleton).As<IClock>();
            // Explicit factory: LocalJsonSaveService has a second constructor taking a
            // directory (for tests), and we want the persistentDataPath one here.
            builder.Register<ISaveService>(_ => new LocalJsonSaveService(), Lifetime.Singleton);
            builder.Register<MockPurchasingService>(Lifetime.Singleton).As<IPurchasingService>();
            builder.Register<StubAudioService>(Lifetime.Singleton).As<IAudioService>();
            builder.Register<StubHapticService>(Lifetime.Singleton).As<IHapticService>();

            // Save lifecycle — knows every module, so it lives with the composition root.
            builder.Register<GameStateCoordinator>(Lifetime.Singleton);

            // Entry point
            builder.RegisterEntryPoint<GameEntryPoint>();
        }

        /// <summary>
        /// The configured curve, or the GDD §6 default if the asset isn't wired yet.
        /// Falling back rather than throwing keeps the game bootable during development,
        /// but it's loud — silently levelling on a different curve than the designer set
        /// is exactly the class of bug this whole seam exists to prevent.
        /// </summary>
        private LevelingCurve ResolveLevelingCurve()
        {
            if (_levelingConfig != null) return _levelingConfig.ToCurve();

            Debug.LogWarning(
                "[GameBootstrapper] No LevelingConfig assigned — falling back to the default " +
                "curve. Assign the asset on the Bootstrap scene's GameBootstrapper.", this);
            return LevelingCurve.Default;
        }
    }
}
