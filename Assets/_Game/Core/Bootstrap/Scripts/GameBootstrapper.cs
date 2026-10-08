using UnityEngine;
using UnityEngine.Serialization;
using KingdomRuler.Systems.Events;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Modules.Navigation;
using KingdomRuler.Systems.Popups;
using KingdomRuler.Systems.Audio;
using KingdomRuler.Systems.Clock;
using KingdomRuler.Systems.Haptics;
using KingdomRuler.Systems.Localization;
using KingdomRuler.Systems.Purchasing;
using KingdomRuler.Systems.Save;
using KingdomRuler.Modules.Laws;
using KingdomRuler.Modules.Laws.Presenters;
using VContainer;
using VContainer.Unity;
using KingdomRuler.Modules.Trade;
using KingdomRuler.Modules.Trade.Presenters;
using KingdomRuler.Modules.Economy;
using KingdomRuler.Modules.Cities;
using KingdomRuler.Modules.RandomOccurrences;

namespace KingdomRuler.Core
{
    public sealed class GameBootstrapper : LifetimeScope
    {
        [Header("Systems")]
        [Tooltip("Icons and name keys for the 6 characteristics. Ledger-owned because Laws, " +
                 "Cities and Random Occurrences all display them — see ARCHITECTURE.md §4.3.")]
        [SerializeField] private CharacteristicRegistry _characteristicRegistry;

        [Tooltip("Icons and name keys for the 6 trade resources. Ledger-owned for the same " +
                 "reason — Trade draws them on warehouses and offers, Cities on purchase costs.")]
        [SerializeField] private TradeResourceRegistry _tradeResourceRegistry;

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
            // Systems
            builder.Register<EventBus>(Lifetime.Singleton);

            // The game clock: tells the time, and ticks every time-driven system four times a
            // second. Registered as an entry point, which also makes it the IClock everyone
            // receives. Each manager with a timer subscribes itself in its constructor.
            builder.RegisterEntryPoint<GameClock>();

            builder.Register<KingdomLedger>(Lifetime.Singleton);

            // Warehouses regenerate on the game clock. Wired here rather than inside the Ledger,
            // which deliberately holds no clock: something outside it always says when time has
            // passed (see KingdomLedger.AccruePassiveResourceRegen).
            builder.RegisterBuildCallback(c =>
                c.Resolve<IClock>().Ticked += c.Resolve<KingdomLedger>().AccruePassiveResourceRegen);

            builder.RegisterInstance(ResolveCharacteristicRegistry());
            builder.RegisterInstance(ResolveTradeResourceRegistry());

            // Navigation module (GDD §13, ARCHITECTURE.md §4.6).
            builder.Register<ScreenNavigator>(Lifetime.Singleton);

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

            // Trade module.
            // Explicit factory for the same reason as Laws: TradeManager has a second
            // constructor taking a System.Random (so tests can pin the offer batch), and
            // VContainer picks the constructor with the most parameters.
            builder.Register(c => new TradeManager(
                    c.Resolve<KingdomLedger>(), c.Resolve<IClock>(), c.Resolve<TradeConfig>()),
                Lifetime.Singleton);
            builder.Register<TradePresenter>(Lifetime.Singleton);
            builder.RegisterInstance(ResolveTradeConfig());

            // Economy module
            builder.Register<EconomyManager>(Lifetime.Singleton);
            builder.RegisterInstance(_economyConfig);

            // Cities module
            builder.Register<CitiesManager>(Lifetime.Singleton);

            // Random Occurrences module (GDD §10)
            builder.Register<RandomOccurrenceManager>(Lifetime.Singleton);
            builder.RegisterInstance(_randomOccurrenceConfig);

            // Services
            // Explicit factory: LocalJsonSaveService has a second constructor taking a
            // directory (for tests), and we want the persistentDataPath one here.
            builder.Register<ISaveService>(_ => new LocalJsonSaveService(), Lifetime.Singleton);
            builder.Register<MockPurchasingService>(Lifetime.Singleton).As<IPurchasingService>();
            builder.Register<StubAudioService>(Lifetime.Singleton).As<IAudioService>();
            builder.Register<StubHapticService>(Lifetime.Singleton).As<IHapticService>();
            builder.Register<UnityLocalizationService>(Lifetime.Singleton).As<ILocalizationService>();

            // Save lifecycle — knows every module, so it lives with the composition root.
            builder.Register<GameStateCoordinator>(Lifetime.Singleton);

            // Popups. PopupSystem is a MonoBehaviour in this scene rather than in Main, so
            // it can be resolved by Presenters that are built here at startup.
            builder.RegisterComponentInHierarchy<PopupSystem>();

            // Entry point
            builder.RegisterEntryPoint<GameEntryPoint>();
        }

        /// <summary>
        /// The wired registry, or an empty one if the asset isn't assigned yet.
        /// </summary>
        /// <remarks>
        /// Never null: registering a null instance would fail every resolve downstream, and
        /// an unassigned registry is a wiring mistake, not a reason for the game to refuse to
        /// start. An empty one degrades honestly — <c>NameKeyFor</c> still derives the right
        /// key, so text keeps working and only the icons are missing.
        /// </remarks>
        private CharacteristicRegistry ResolveCharacteristicRegistry()
        {
            if (_characteristicRegistry != null) return _characteristicRegistry;

            Debug.LogWarning(
                "[GameBootstrapper] No CharacteristicRegistry assigned — characteristic icons " +
                "will be missing. Assign the asset on the Bootstrap scene's GameBootstrapper.",
                this);
            return ScriptableObject.CreateInstance<CharacteristicRegistry>();
        }

        /// <summary>
        /// The wired trade-resource registry, or an empty one if the asset isn't assigned yet.
        /// </summary>
        /// <remarks>
        /// Same contract as <see cref="ResolveCharacteristicRegistry"/>, for the same reason:
        /// registering null would fail every resolve downstream, while an empty registry degrades
        /// honestly — <c>NameKeyFor</c> still derives the right key, so text keeps working and
        /// only the icons are absent.
        /// </remarks>
        /// <summary>
        /// The wired Trade config, or a defaults instance if the asset isn't assigned yet.
        /// </summary>
        /// <remarks>
        /// Same contract as the registries above. Without this an unassigned field registered
        /// null and <c>TradeManager</c>'s constructor threw at startup, taking the whole game
        /// down rather than degrading to defaults.
        /// </remarks>
        private TradeConfig ResolveTradeConfig()
        {
            if (_tradeConfig != null) return _tradeConfig;

            Debug.LogWarning(
                "[GameBootstrapper] No TradeConfig assigned — falling back to defaults. " +
                "Assign the asset on the Bootstrap scene's GameBootstrapper.", this);
            return ScriptableObject.CreateInstance<TradeConfig>();
        }

        private TradeResourceRegistry ResolveTradeResourceRegistry()
        {
            if (_tradeResourceRegistry != null) return _tradeResourceRegistry;

            Debug.LogWarning(
                "[GameBootstrapper] No TradeResourceRegistry assigned — trade resource icons " +
                "will be missing. Assign the asset on the Bootstrap scene's GameBootstrapper.",
                this);
            return ScriptableObject.CreateInstance<TradeResourceRegistry>();
        }
    }
}
