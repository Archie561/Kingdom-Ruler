using System.Collections.Generic;
using TMPro;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Shared.Text;
using KingdomRuler.Modules.Laws.Presenters;

namespace KingdomRuler.Modules.Laws.Views
{
    /// <summary>
    /// Root coordinator MonoBehaviour for the Laws screen.
    ///
    /// Responsibilities:
    ///   - Receives LawsPresenter via VContainer [Inject] method injection.
    ///   - Subscribes to presenter events; drives sub-views on state changes.
    ///   - Forwards user intents (swipe, buttons) to the Presenter.
    ///   - Renders the countdown. It does NOT advance it: the replenishment timer is
    ///     driven by AccrualDriver so the mechanic keeps running whether or not this
    ///     screen exists (ARCHITECTURE.md §4.5). This View is display-only.
    ///
    /// Animation contract (CRITICAL — no business logic gated on animations):
    ///   LawCardView.OnSwiped      → immediately forwarded to Presenter (logic happens now).
    ///   LawCardView.OnSwipeAnimationComplete → COSMETIC cue to reveal the next card.
    ///   Refresh() always updates all non-card state. The card visual is updated either
    ///   from Refresh() (when no swipe is in flight) or from OnSwipeAnimationComplete.
    ///
    /// Canvas layout (LawsScreen.prefab):
    ///   LawsScreen  — static panel: characteristics, timer row, refill button
    ///   ├── CardCanvas (nested Canvas, Override Sorting) — isolates card animation redraws
    ///   │   └── LawCard (LawCardView)
    ///   └── WaitingOverlay — "The council is preparing new decrees"
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public sealed class LawsView : MonoBehaviour
    {
        // ── Sub-view references (wired in prefab Inspector) ───────────────────────

        [Header("Card Area")]
        [Tooltip("The active law card view (sits on its own nested Canvas).")]
        [SerializeField] private LawCardView _cardView;

        [Tooltip("Held-card readout, e.g. \"6/8\" — counts the active card too.")]
        [SerializeField] private TextMeshProUGUI _cardCountLabel;

        [Header("Characteristic Bars")]
        [Tooltip("All 6 CharacteristicBarView instances. Order does not matter — each bar " +
                 "declares which characteristic it renders, and they are bound by that.")]
        [SerializeField] private CharacteristicBarView[] _characteristicBars;

        [Header("Timer & Refill")]
        [SerializeField] private TextMeshProUGUI _timerLabel;
        [SerializeField] private Button          _refillButton;
        [SerializeField] private TextMeshProUGUI _refillCostLabel;

        [Header("Waiting State")]
        [Tooltip("Overlay shown when no cards are available and none are replenishing.")]
        [SerializeField] private GameObject _waitingOverlay;

        // ── Injected dependency ───────────────────────────────────────────────────

        private LawsPresenter _presenter;

        /// <summary>
        /// Bars indexed by the characteristic each one declares. Built once from
        /// <see cref="CharacteristicBarView.Type"/> so a designer reordering the
        /// serialized array can't silently render one characteristic's data in
        /// another's bar.
        /// </summary>
        private readonly Dictionary<CharacteristicType, CharacteristicBarView> _barsByType = new();

        // Last values pushed to the labels, so a refresh triggered by an unrelated change
        // doesn't re-issue identical strings and dirty the canvas for nothing.
        private int _shownTimerSeconds   = -1;
        private int _shownAvailableCount = -1;
        private int _shownRefillCost     = -1;

        [Inject]
        public void Construct(LawsPresenter presenter)
        {
            _presenter = presenter;
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        private void Start()
        {
            // Nothing injected us. Overwhelmingly this means the game was started from
            // Main instead of Bootstrap, so the root LifetimeScope never existed. Say so
            // once and switch off, rather than throwing a NullReferenceException here and
            // then again from Update() on every frame, which buries the actual cause.
            if (_presenter == null)
            {
                Debug.LogError(
                    "[LawsView] No LawsPresenter was injected — the Laws screen is disabled. " +
                    "Enter Play mode from the Bootstrap scene; Main is loaded additively from " +
                    "there and only then do its Views get injected.", this);
                enabled = false;
                return;
            }

            BuildBarLookup();
            ValidateSubViews();
            WireSubViewEvents();

            _presenter.OnStateChanged            += Refresh;
            _presenter.OnCharacteristicLeveledUp += OnCharacteristicLeveledUp;
            _presenter.OnCardArrived             += OnCardArrived;

            _refillButton.onClick.AddListener(OnRefillButtonPressed);

            // OnEnable may have run before injection completed; assert the real state now.
            _presenter.SetScreenVisible(isActiveAndEnabled);

            Refresh(); // initial render
        }

        private void OnEnable()
        {
            // Injection happens before Start, but OnEnable can run first on the very
            // first frame — guard rather than assume ordering.
            _presenter?.SetScreenVisible(true);
        }

        private void OnDisable()
        {
            _presenter?.SetScreenVisible(false);
        }

        private void OnDestroy()
        {
            // Unhook everything, not just the presenter. The presenter is a root-scope
            // singleton that outlives this View, so a missed unsubscribe there leaks into
            // the next scene load.
            UnwireSubViewEvents();
            _refillButton.onClick.RemoveListener(OnRefillButtonPressed);

            if (_presenter == null) return;
            _presenter.OnStateChanged            -= Refresh;
            _presenter.OnCharacteristicLeveledUp -= OnCharacteristicLeveledUp;
            _presenter.OnCardArrived             -= OnCardArrived;
        }

        private void Update()
        {
            // Display only. The replenishment timer itself is advanced by AccrualDriver,
            // not from here — the mechanic must keep running whether or not this screen
            // exists (ARCHITECTURE.md §4.5).
            if (!_presenter.IsReplenishing) return;

            // The countdown only ever shows whole seconds, so writing it every frame
            // rewrites the same string ~60×/second. Each write dirties the TMP mesh and
            // forces a rebuild of the *static* LawsScreen canvas — the very canvas the nested
            // CardCanvas was split off to protect (ARCHITECTURE.md §2, §9).
            int secondsRemaining = Mathf.CeilToInt(_presenter.SecondsUntilNextCard);
            if (secondsRemaining == _shownTimerSeconds) return;

            _shownTimerSeconds = secondsRemaining;
            _timerLabel.SetText(TimeFormat.MinutesSeconds(secondsRemaining));
        }

        // ── Full refresh (driven by Presenter.OnStateChanged) ─────────────────────

        private void Refresh()
        {
            // ── Held-card readout ────────────────────────────────────────────────
            int available = _presenter.AvailableCardCount;
            if (available != _shownAvailableCount)
            {
                _shownAvailableCount = available;
                _cardCountLabel.SetText($"{available}/{_presenter.MaxCardCount}");
            }

            // ── Timer label ──────────────────────────────────────────────────────
            bool replenishing = _presenter.IsReplenishing;
            _timerLabel.gameObject.SetActive(replenishing);
            // Force the next Update to write, so a timer that stops and restarts doesn't
            // sit on a stale value that happens to match the cached one.
            if (!replenishing) _shownTimerSeconds = -1;

            // ── Waiting overlay ──────────────────────────────────────────────────
            _waitingOverlay.SetActive(_presenter.IsWaiting);

            // ── Card visual ──────────────────────────────────────────────────────
            // If a swipe-off animation is still playing, let it finish.
            // OnSwipeAnimationComplete will call ShowOrHideCard() once the old card
            // has flown off screen.  For every other refresh path (initial load,
            // refill, characteristic buy-up), we update immediately.
            if (!_cardView.IsSwipeAnimating)
                ShowOrHideCard();

            // ── Refill button ────────────────────────────────────────────────────
            _refillButton.interactable = _presenter.CanAffordRefill;
            int refillCost = _presenter.RefillCost;
            if (refillCost != _shownRefillCost)
            {
                _shownRefillCost = refillCost;
                _refillCostLabel.SetText(refillCost.ToString());
            }

            // ── Characteristic bars ──────────────────────────────────────────────
            foreach (var entry in _barsByType)
                entry.Value.UpdateDisplay(_presenter.GetCharacteristicDisplay(entry.Key));
        }

        // ── Card show/hide helper ─────────────────────────────────────────────────

        private void ShowOrHideCard()
        {
            if (_presenter.TryGetActiveCardDisplay(out var display))
            {
                _cardView.Populate(display);
                _cardView.gameObject.SetActive(true);
            }
            else
            {
                _cardView.gameObject.SetActive(false);
            }
        }

        // ── Event callbacks ───────────────────────────────────────────────────────

        private void OnCharacteristicLeveledUp(CharacteristicType type)
        {
            var bar = FindBarForType(type);
            bar?.PlayLevelUpCelebration();
        }

        /// <summary>
        /// Called the moment the swipe threshold is crossed.
        /// Business logic (Presenter.OnSwipeAccepted/Rejected) runs here — immediately,
        /// before any animation. This is the critical invariant: logic never waits for feel.
        /// </summary>
        private void OnCardSwiped(bool accepted)
        {
            if (accepted)
                _presenter.OnSwipeAccepted();
            else
                _presenter.OnSwipeRejected();
            // Note: card content will be updated in OnSwipeAnimationComplete once the
            // old card has visually left the screen.
        }

        /// <summary>
        /// Cosmetic-only callback — the swipe-off animation has finished.
        /// Now safe to populate and animate-in the next card.
        /// MUST NOT contain any business logic.
        /// </summary>
        private void OnSwipeAnimationComplete()
        {
            ShowOrHideCard();
            if (_presenter.HasActiveCard)
                _cardView.AnimateIn();
        }

        // Opens the confirmation. Nothing is spent and no card appears until the player
        // confirms, so there is deliberately nothing cosmetic to do here — the card's
        // entrance is driven by Presenter.OnCardArrived instead.
        private void OnRefillButtonPressed() => _presenter.OnCrystalRefillRequested().Forget();

        /// <summary>
        /// A card turned up. Play its entrance, whatever caused it to arrive.
        /// </summary>
        /// <remarks>
        /// Guarded on the swipe animation because OnSwipeAnimationComplete owns the reveal in
        /// that case: letting both run would restart the scale-in mid-flight.
        /// </remarks>
        private void OnCardArrived()
        {
            if (!_cardView.IsSwipeAnimating)
                _cardView.AnimateIn();
        }

        // ── Wiring helpers ────────────────────────────────────────────────────────

        private void WireSubViewEvents()
        {
            // Method group, not a lambda: a lambda creates a fresh delegate each time and
            // could never be unsubscribed in OnDestroy.
            _cardView.OnSwiped                 += OnCardSwiped;
            _cardView.OnSwipeAnimationComplete += OnSwipeAnimationComplete;

            // CharacteristicBarView.OnDialPressed is deliberately not subscribed yet. It used
            // to run the crystal buy-up directly, which stopped being acceptable when the dial
            // became the whole tap target and the price label went away — a large, easy-to-hit
            // control that spends a premium currency with nothing shown first. The buy-up
            // belongs in the characteristic detail panel; when that exists, subscribe here and
            // open it, and have the panel call Presenter.OnBuyUpRequested behind its own
            // confirm. Until then a dial tap intentionally does nothing.
        }

        private void UnwireSubViewEvents()
        {
            if (_cardView != null)
            {
                _cardView.OnSwiped                 -= OnCardSwiped;
                _cardView.OnSwipeAnimationComplete -= OnSwipeAnimationComplete;
            }
        }

        private void BuildBarLookup()
        {
            _barsByType.Clear();
            if (_characteristicBars == null) return;

            foreach (var bar in _characteristicBars)
            {
                if (bar == null) continue;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (_barsByType.ContainsKey(bar.Type))
                    Debug.LogError(
                        $"[LawsView] Two characteristic bars are both bound to {bar.Type}. " +
                        "Each bar's Type must be unique — one characteristic will not be shown.", this);
#endif
                _barsByType[bar.Type] = bar;
            }
        }

        private CharacteristicBarView FindBarForType(CharacteristicType type) =>
            _barsByType.TryGetValue(type, out var bar) ? bar : null;

        private void ValidateSubViews()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_cardView == null)
                Debug.LogError("[LawsView] _cardView is not assigned.", this);
            if (_cardCountLabel == null)
                Debug.LogError("[LawsView] _cardCountLabel is not assigned.", this);
            if (_timerLabel == null)
                Debug.LogError("[LawsView] _timerLabel is not assigned.", this);
            if (_refillButton == null)
                Debug.LogError("[LawsView] _refillButton is not assigned.", this);
            if (_refillCostLabel == null)
                Debug.LogError("[LawsView] _refillCostLabel is not assigned.", this);
            if (_waitingOverlay == null)
                Debug.LogError("[LawsView] _waitingOverlay is not assigned.", this);

            // Checking coverage by type, not array length: six entries that all point at
            // the same characteristic would pass a length check and render nonsense.
            foreach (CharacteristicType type in System.Enum.GetValues(typeof(CharacteristicType)))
            {
                if (!_barsByType.ContainsKey(type))
                    Debug.LogError($"[LawsView] No CharacteristicBarView is bound to {type}.", this);
            }
#endif
        }
    }
}
