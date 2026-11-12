using System;
using UnityEngine;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Systems.Save;
using KingdomRuler.Modules.Laws;
using KingdomRuler.Modules.Trade;
using KingdomRuler.Modules.Economy;

namespace KingdomRuler.Core
{
    /// <summary>
    /// Owns the game's save lifecycle: hydrating every system on startup, and writing
    /// state back out at the moments the player might not come back from.
    /// </summary>
    /// <remarks>
    /// In <c>Core</c> because it is the one class that legitimately knows every module
    /// (<c>ARCHITECTURE.md</c> §4.1). Each module maps its own state to and from its DTO; this
    /// only sequences those calls.
    /// </remarks>
    public sealed class GameStateCoordinator : IDisposable
    {
        private readonly ISaveService            _saveService;
        private readonly KingdomLedger           _ledger;
        private readonly LawsManager             _laws;
        private readonly TradeManager            _trade;
        private readonly EconomyManager          _economy;

        private bool _isHydrated;

        public GameStateCoordinator(
            ISaveService            saveService,
            KingdomLedger           ledger,
            LawsManager             laws,
            TradeManager            trade,
            EconomyManager          economy)
        {
            _saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
            _ledger      = ledger      ?? throw new ArgumentNullException(nameof(ledger));
            _laws        = laws        ?? throw new ArgumentNullException(nameof(laws));
            _trade       = trade       ?? throw new ArgumentNullException(nameof(trade));
            _economy     = economy     ?? throw new ArgumentNullException(nameof(economy));

            Application.focusChanged += OnFocusChanged;
            Application.quitting     += OnQuitting;
        }

        /// <summary>
        /// Bring every system up: from the save file if there is a readable one, otherwise
        /// from a fresh start. Call once, before any View exists.
        /// </summary>
        /// <remarks>Load and fresh-start are mutually exclusive by construction.</remarks>
        public void LoadOrInitialize()
        {
            if (_isHydrated)
            {
                Debug.LogWarning("[GameStateCoordinator] LoadOrInitialize called twice — ignoring.");
                return;
            }
            _isHydrated = true;

            var state = _saveService.Load();
            if (state == null)
            {
                StartNewGame();
                return;
            }

            _ledger.LoadFromDto(state.Ledger);

            // Each module settles its own offline progress inside LoadFromDto. Warehouse regen is
            // deliberately not settled here: Trade does it in its own load, once capacities are final.
            if (state.Laws              != null) _laws.LoadFromDto(state.Laws);
            else                                 _laws.InitializeNewGame();
            if (state.Trade             != null) _trade.LoadFromDto(state.Trade);
            else                                 _trade.InitializeNewGame();

            // After the Ledger: a save from before Economy existed gets the starting gold added to
            // the balance it already has.
            if (state.Economy           != null) _economy.LoadFromDto(state.Economy);
            else                                 _economy.InitializeNewGame();
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
                Economy           = _economy.ToDto()
            });
        }

        public void Dispose()
        {
            Application.focusChanged -= OnFocusChanged;
            Application.quitting     -= OnQuitting;
        }

        // ── Private ───────────────────────────────────────────────────────────────

        private void StartNewGame()
        {
            _laws.InitializeNewGame();
            // Seeds base warehouse capacities, the first batch of offers, and the regen baseline.
            _trade.InitializeNewGame();
            // Grants the starting gold: exactly the cheapest business's price (GDD §8).
            _economy.InitializeNewGame();
        }

        private void OnFocusChanged(bool hasFocus)
        {
            // The last dependable moment to save: on mobile the OS may kill a backgrounded app
            // without any further callback.
            if (!hasFocus) Save();
        }

        private void OnQuitting() => Save();
    }
}
