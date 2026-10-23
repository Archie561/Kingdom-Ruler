using System;
using UnityEngine;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Systems.Clock;
using KingdomRuler.Systems.Save;
using KingdomRuler.Modules.Laws;
using KingdomRuler.Modules.Trade;

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

        private bool _isHydrated;

        public GameStateCoordinator(
            ISaveService            saveService,
            IClock                  clock,
            KingdomLedger           ledger,
            LawsManager             laws,
            TradeManager            trade)
        {
            _saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
            _clock       = clock       ?? throw new ArgumentNullException(nameof(clock));
            _ledger      = ledger      ?? throw new ArgumentNullException(nameof(ledger));
            _laws        = laws        ?? throw new ArgumentNullException(nameof(laws));
            _trade       = trade       ?? throw new ArgumentNullException(nameof(trade));

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

            // Resource regen is deliberately NOT accrued here. It used to be, and it ran against
            // the capacity the ledger DTO happened to carry — which TradeManager.LoadFromDto then
            // overwrote from the warehouse level moments later, so the two fought. Trade now
            // settles regen inside its own load, after capacity is final, which makes the
            // invariant "regen never accrues against a capacity that is about to change"
            // structural rather than a matter of call order here.

            // Each module fast-forwards its own offline progress inside LoadFromDto.
            if (state.Laws              != null) _laws.LoadFromDto(state.Laws);
            else                                 _laws.InitializeNewGame();
            if (state.Trade             != null) _trade.LoadFromDto(state.Trade);
            else                                 _trade.InitializeNewGame();
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
                Trade             = _trade.ToDto()
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
