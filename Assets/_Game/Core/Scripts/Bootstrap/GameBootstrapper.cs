using UnityEngine;
using KingdomRuler.Shared.Ledger;
using KingdomRuler.Shared.Services;
using KingdomRuler.Modules.Laws;
using VContainer;
using VContainer.Unity;
using KingdomRuler.Modules.Trade;
using KingdomRuler.Modules.Economy;
using KingdomRuler.Modules.Cities;
using KingdomRuler.Modules.Events;

namespace KingdomRuler.Core
{
    public sealed class GameBootstrapper : LifetimeScope
    {
        [SerializeField] private LawsConfig _lawsConfig;
        [SerializeField] private TradeConfig _tradeConfig;
        [SerializeField] private EconomyConfig _economyConfig;
        [SerializeField] private EventsConfig _eventsConfig;

        protected override void Configure(IContainerBuilder builder)
        {
            // Core
            builder.Register<EventBus>(Lifetime.Singleton);
            builder.Register<KingdomLedger>(Lifetime.Singleton);

            // Laws module
            builder.Register<LawsManager>(Lifetime.Singleton);
            builder.RegisterInstance(_lawsConfig);

            // Trade module
            builder.Register<TradeManager>(Lifetime.Singleton);
            builder.RegisterInstance(_tradeConfig);

            // Economy module
            builder.Register<EconomyManager>(Lifetime.Singleton);
            builder.RegisterInstance(_economyConfig);

            // Cities module
            builder.Register<CitiesManager>(Lifetime.Singleton);

            // Events module
            builder.Register<EventsManager>(Lifetime.Singleton);
            builder.RegisterInstance(_eventsConfig);

            // Services
            builder.Register<SystemClock>(Lifetime.Singleton).As<IClock>();
            builder.Register<LocalJsonSaveService>(Lifetime.Singleton).As<ISaveService>();
            builder.Register<MockPurchasingService>(Lifetime.Singleton).As<IPurchasingService>();
            builder.Register<StubAudioService>(Lifetime.Singleton).As<IAudioService>();
            builder.Register<StubHapticService>(Lifetime.Singleton).As<IHapticService>();

            // Entry point
            builder.RegisterEntryPoint<GameEntryPoint>();
        }
    }
}
