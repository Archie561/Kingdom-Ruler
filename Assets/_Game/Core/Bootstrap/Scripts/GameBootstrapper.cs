using UnityEngine;
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
using KingdomRuler.Modules.Laws.UI;
using VContainer;
using VContainer.Unity;
using KingdomRuler.Modules.Trade;
using KingdomRuler.Modules.Trade.UI;

namespace KingdomRuler.Core
{
    public sealed class GameBootstrapper : LifetimeScope
    {
        [Header("Systems")]
        [Tooltip("Icons and name keys for the 6 characteristics (ARCHITECTURE.md §4.3).")]
        [SerializeField] private CharacteristicRegistry _characteristicRegistry;

        [Tooltip("Icons and name keys for the 6 trade resources (ARCHITECTURE.md §4.3).")]
        [SerializeField] private TradeResourceRegistry _tradeResourceRegistry;

        [Header("Modules")]
        [SerializeField] private LawsConfig    _lawsConfig;
        [SerializeField] private TradeConfig   _tradeConfig;

        protected override void Configure(IContainerBuilder builder)
        {
            // Systems
            builder.Register<EventBus>(Lifetime.Singleton);

            // An entry point, so VContainer ticks it; it is also the IClock everyone receives.
            builder.RegisterEntryPoint<GameClock>();

            builder.Register<KingdomLedger>(Lifetime.Singleton);

            // Wired here because the Ledger deliberately holds no clock (KingdomLedger.AccruePassiveResourceRegen).
            builder.RegisterBuildCallback(c =>
                c.Resolve<IClock>().Ticked += c.Resolve<KingdomLedger>().AccruePassiveResourceRegen);

            builder.RegisterInstance(ResolveCharacteristicRegistry());
            builder.RegisterInstance(ResolveTradeResourceRegistry());

            // Navigation module (GDD §13, ARCHITECTURE.md §4.6).
            builder.Register<ScreenNavigator>(Lifetime.Singleton);

            // Laws module.
            builder.RegisterInstance(_lawsConfig);
            builder.Register<LawsManager>(Lifetime.Singleton);
            builder.Register<LawsPresenter>(Lifetime.Singleton);

            // Trade module.
            builder.RegisterInstance(ResolveTradeConfig());
            builder.Register<TradeManager>(Lifetime.Singleton);
            builder.Register<TradePresenter>(Lifetime.Singleton);

            // Services
            // Explicit factory: picks the persistentDataPath constructor over the test-only one.
            builder.Register<ISaveService>(_ => new LocalJsonSaveService(), Lifetime.Singleton);
            builder.Register<MockPurchasingService>(Lifetime.Singleton).As<IPurchasingService>();
            builder.Register<StubAudioService>(Lifetime.Singleton).As<IAudioService>();
            builder.Register<StubHapticService>(Lifetime.Singleton).As<IHapticService>();
            builder.Register<UnityLocalizationService>(Lifetime.Singleton).As<ILocalizationService>();

            // Save lifecycle — knows every module, so it lives with the composition root.
            builder.Register<GameStateCoordinator>(Lifetime.Singleton);

            // In the Bootstrap scene, not Main, so Presenters built at startup can resolve it.
            builder.RegisterComponentInHierarchy<PopupSystem>();

            builder.RegisterEntryPoint<GameEntryPoint>();
        }

        // The Resolve… methods never return null: registering null would fail every resolve
        // downstream, and a missing asset is a wiring mistake, not a reason to refuse to start. An
        // empty registry still derives every name key, so only the icons go missing; an empty
        // config runs on its defaults.

        private CharacteristicRegistry ResolveCharacteristicRegistry()
        {
            if (_characteristicRegistry != null) return _characteristicRegistry;

            Debug.LogWarning(
                "[GameBootstrapper] No CharacteristicRegistry assigned — characteristic icons " +
                "will be missing. Assign the asset on the Bootstrap scene's GameBootstrapper.",
                this);
            return ScriptableObject.CreateInstance<CharacteristicRegistry>();
        }

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
