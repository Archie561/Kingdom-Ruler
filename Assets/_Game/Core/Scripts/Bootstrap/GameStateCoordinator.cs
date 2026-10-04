using System;
using UnityEngine;
using KingdomRuler.Shared.Ledger;
using KingdomRuler.Shared.Services;
using KingdomRuler.Modules.Laws;
using KingdomRuler.Modules.Trade;
using KingdomRuler.Modules.Economy;
using KingdomRuler.Modules.Cities;
using KingdomRuler.Modules.RandomOccurrences;

namespace KingdomRuler.Core
{
    /// <summary>
    /// Owns the game's save lifecycle: hydrating every system on startup, and writing
    /// state back out at the moments the player might not come back from.
    /// </summary>
    /// <remarks>
    /// This lives beside the composition root because it is the one class that legitimately
    /// knows every module — the modules themselves stay unaware of each other
    /// (ARCHITECTURE.md §4.1, §4.4). Each module maps its own state to and from its own DTO;
    /// this type only sequences those calls and owns the file boundary.
    /// </remarks>
    public sealed class GameStateCoordinator : IDisposable
    {
        private readonly ISaveService            _saveService;
        private readonly IClock                  _clock;
        private readonly KingdomLedger           _ledger;
        private readonly LawsManager             _laws;
        private readonly TradeManager            _trade;
        private readonly EconomyManager          _economy;
        private readonly CitiesManager           _cities;
        private readonly RandomOccurrenceManager _occurrences;
        private readonly LawsConfig              _lawsConfig;

        private bool _isHydrated;

        public GameStateCoordinator(
            ISaveService            saveService,
            IClock                  clock,
            KingdomLedger           ledger,
            LawsManager             laws,
            TradeManager            trade,
            EconomyManager          economy,
            CitiesManager           cities,
            RandomOccurrenceManager occurrences,
            LawsConfig              lawsConfig)
        {
            _saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
            _clock       = clock       ?? throw new ArgumentNullException(nameof(clock));
            _ledger      = ledger      ?? throw new ArgumentNullException(nameof(ledger));
            _laws        = laws        ?? throw new ArgumentNullException(nameof(laws));
            _trade       = trade       ?? throw new ArgumentNullException(nameof(trade));
            _economy     = economy     ?? throw new ArgumentNullException(nameof(economy));
            _cities      = cities      ?? throw new ArgumentNullException(nameof(cities));
            _occurrences = occurrences ?? throw new ArgumentNullException(nameof(occurrences));
            _lawsConfig  = lawsConfig  ?? throw new ArgumentNullException(nameof(lawsConfig));

            Application.focusChanged += OnFocusChanged;
            Application.quitting     += OnQuitting;
        }

        /// <summary>
        /// Bring every system up: from the save file if there is a readable one, otherwise
        /// from a fresh start. Call once, before any View exists.
        /// </summary>
        /// <remarks>
        /// Load and fresh-start are mutually exclusive by construction. Content pools are
        /// seeded first because a module's LoadFromDto resolves saved ids against its pool —
        /// with an empty pool, every saved reference would silently drop.
        /// </remarks>
        public void LoadOrInitialize()
        {
            if (_isHydrated)
            {
                Debug.LogWarning("[GameStateCoordinator] LoadOrInitialize called twice — ignoring.");
                return;
            }
            _isHydrated = true;

            SeedContentPools();

            var state = _saveService.Load();
            if (state == null)
            {
                StartNewGame();
                return;
            }

            _ledger.LoadFromDto(state.Ledger);

            // Resource regen is deliberately NOT accrued here. It used to be, and it ran against
            // the capacity the ledger DTO happened to carry — which TradeManager.LoadFromDto then
            // overwrote from the warehouse level moments later, so the two fought. Trade now
            // settles regen inside its own load, after capacity is final, which makes the
            // invariant "regen never accrues against a capacity that is about to change"
            // structural rather than a matter of call order here.

            // Each module fast-forwards its own offline progress inside LoadFromDto.
            if (state.Laws              != null) _laws.LoadFromDto(state.Laws);
            else                                 _laws.InitializeCardPool();
            if (state.Trade             != null) _trade.LoadFromDto(state.Trade);
            else                                 _trade.InitializeNewGame();
            if (state.Economy           != null) _economy.LoadFromDto(state.Economy);
            if (state.Cities            != null) _cities.LoadFromDto(state.Cities);
            if (state.RandomOccurrences != null) _occurrences.LoadFromDto(state.RandomOccurrences);
        }

        /// <summary>Write current state to disk. Safe to call at any time.</summary>
        public bool Save()
        {
            if (!_isHydrated)
            {
                // Saving before hydrating would overwrite a real save with a blank game.
                Debug.LogWarning("[GameStateCoordinator] Save requested before load — refusing.");
                return false;
            }

            return _saveService.Save(new GameStateDto
            {
                SchemaVersion     = GameStateDto.CurrentSchemaVersion,
                Ledger            = _ledger.ToDto(),
                Laws              = _laws.ToDto(),
                Trade             = _trade.ToDto(),
                Economy           = _economy.ToDto(),
                Cities            = _cities.ToDto(),
                RandomOccurrences = _occurrences.ToDto()
            });
        }

        public void Dispose()
        {
            Application.focusChanged -= OnFocusChanged;
            Application.quitting     -= OnQuitting;
        }

        // ── Private ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Register each module's content so saved ids can be resolved back to assets.
        /// </summary>
        /// <remarks>
        /// Only Laws has authored content today. Economy, Cities and Random Occurrences all
        /// expose an Initialize* method but have no content registry on their config yet, so
        /// there is nothing to seed them from — wire those up when their content lands
        /// rather than inventing a schema for them here.
        /// </remarks>
        private void SeedContentPools()
        {
            // LawsManager reads AllCards from its own config inside InitializeCardPool and
            // LoadFromDto, so nothing to pass here; guard the empty case for a clear message.
            if (_lawsConfig.AllCards == null || _lawsConfig.AllCards.Length == 0)
            {
                Debug.LogWarning(
                    "[GameStateCoordinator] LawsConfig has no cards assigned — the Laws screen " +
                    "will sit in its waiting state. Add LawCardDefinition assets to LawsConfig.AllCards.");
            }
        }

        private void StartNewGame()
        {
            _laws.InitializeCardPool();
            // Seeds base warehouse capacities, the first batch of offers, and the regen baseline.
            _trade.InitializeNewGame();
        }

        private void OnFocusChanged(bool hasFocus)
        {
            // Losing focus is the closest thing to a reliable "about to be backgrounded"
            // signal available from plain C#. On mobile the OS may kill the app without any
            // further callback, so this is the last dependable chance to persist.
            if (!hasFocus) Save();
        }

        private void OnQuitting() => Save();
    }
}
