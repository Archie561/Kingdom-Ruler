using System;
using KingdomRuler.Core;
using KingdomRuler.Shared.Ledger;
using KingdomRuler.Shared.Services;
using KingdomRuler.Modules.Laws.Domain;

namespace KingdomRuler.Modules.Laws.Presenters
{
    /// <summary>
    /// Mediates between LawsManager (Model) and LawsView (View).
    /// Plain C# — no MonoBehaviour, no DOTween, fully unit-testable.
    ///
    /// Lifecycle:
    ///   1. LawsView subscribes to OnStateChanged / OnCharacteristicLeveledUp.
    ///   2. LawsManager raises QueueChanged → Presenter fires OnStateChanged.
    ///   3. LawsView calls Tick() every Update frame for the countdown label only.
    ///   4. LawsView forwards user interactions via OnSwipeAccepted / etc.
    /// </summary>
    public sealed class LawsPresenter : IDisposable
    {
        // ── SFX identifiers ────────────────────────────────────────────────────────
        private static class SfxIds
        {
            public const string CardAccept   = "sfx_laws_card_accept";
            public const string CardReject   = "sfx_laws_card_reject";
            public const string CardArrive   = "sfx_laws_card_arrive";
            public const string LevelUp      = "sfx_laws_level_up";
            public const string CrystalSpend = "sfx_laws_crystal_spend";
        }

        private readonly LawsManager    _manager;
        private readonly KingdomLedger  _ledger;
        private readonly EventBus       _eventBus;
        private readonly IAudioService  _audio;
        private readonly IHapticService _haptics;

        /// <summary>Whether a card was active as of the last notification, to spot arrivals.</summary>
        private bool _hadActiveCard;

        /// <summary>Whether the Laws screen is currently on-screen. Gates audio only.</summary>
        private bool _isScreenVisible;

        // ── Events the View subscribes to ─────────────────────────────────────────

        /// <summary>
        /// Fired whenever the laws state changes in a way that requires a full View refresh.
        /// Driven by LawsManager.QueueChanged and by explicit user actions.
        /// </summary>
        public event Action OnStateChanged;

        /// <summary>
        /// Fired when a characteristic levels up. Argument is the type that leveled.
        /// The View uses this to trigger per-bar celebration animations.
        /// </summary>
        public event Action<CharacteristicType> OnCharacteristicLeveledUp;

        // ── Read-only state the View polls ────────────────────────────────────────

        /// <summary>Whether a card is currently being presented to the player.</summary>
        public bool HasActiveCard => _manager.HasActiveCard;

        /// <summary>
        /// Build the render-ready data for the active card. Returns false when there is
        /// none, in which case <paramref name="display"/> is meaningless.
        /// </summary>
        /// <remarks>
        /// The View deliberately never sees the LawCardDefinition asset — everything it
        /// draws is turned into strings here, which is where localization will hook in.
        /// </remarks>
        public bool TryGetActiveCardDisplay(out LawCardDisplayData display)
        {
            var card = _manager.ActiveCard;
            if (card == null)
            {
                display = default;
                return false;
            }

            display = new LawCardDisplayData(
                card.CardId,
                card.TitleKey,
                card.FlavorTextKey,
                BuildEffectsSummary(card.AcceptEffects),
                BuildEffectsSummary(card.RejectEffects));
            return true;
        }

        /// <summary>
        /// How many slots have completed their replenishment timer but haven't
        /// drawn a card yet (active slot was occupied). Shows as "+N" badge in the UI.
        /// </summary>
        public int QueuedCardCount => _manager.ReadyCardCount;

        /// <summary>True when no card is shown and nothing is replenishing.</summary>
        public bool IsWaiting => !_manager.HasActiveCard && _manager.CardsReplenishing == 0;

        /// <summary>True when at least one slot is counting down.</summary>
        public bool IsReplenishing => _manager.CardsReplenishing > 0;

        /// <summary>Seconds until the next replenishment slot fires.</summary>
        public float SecondsUntilNextCard => _manager.GetSecondsUntilNextCard();

        /// <summary>
        /// Crystal cost to instant-complete all pending replenishment timers.
        /// Read from the Manager rather than recomputed, so the price shown is by
        /// construction the price charged.
        /// </summary>
        public int RefillCost => _manager.RefillCost;

        /// <summary>True if at least one slot is replenishing AND the player can afford it.</summary>
        public bool CanAffordRefill => _manager.CardsReplenishing > 0 && _ledger.Crystals >= RefillCost;

        // ── Constructor ───────────────────────────────────────────────────────────

        public LawsPresenter(
            LawsManager    manager,
            KingdomLedger  ledger,
            EventBus       eventBus,
            IAudioService  audio,
            IHapticService haptics)
        {
            _manager  = manager  ?? throw new ArgumentNullException(nameof(manager));
            _ledger   = ledger   ?? throw new ArgumentNullException(nameof(ledger));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _audio    = audio    ?? throw new ArgumentNullException(nameof(audio));
            _haptics  = haptics  ?? throw new ArgumentNullException(nameof(haptics));

            _hadActiveCard = _manager.HasActiveCard;

            // CharacteristicLeveledUp is a cross-module ledger event, so it comes off the
            // bus. The queue change is Laws-internal, so it comes straight off the Manager
            // this Presenter already holds (ARCHITECTURE.md §4.2).
            _eventBus.Subscribe<CharacteristicLeveledUp>(HandleCharacteristicLeveledUp);
            _manager.QueueChanged += HandleQueueChanged;
        }

        public void Dispose()
        {
            _eventBus.Unsubscribe<CharacteristicLeveledUp>(HandleCharacteristicLeveledUp);
            _manager.QueueChanged -= HandleQueueChanged;
        }

        // ── Screen visibility ─────────────────────────────────────────────────────

        /// <summary>
        /// Told by the View when the Laws screen is shown or hidden.
        /// </summary>
        /// <remarks>
        /// Only audio depends on this. The queue keeps advancing and the Presenter keeps
        /// tracking it while the player is on another tab — so the screen is correct the
        /// instant they come back — but a card quietly arriving on a screen nobody is
        /// looking at should not make a noise.
        /// </remarks>
        public void SetScreenVisible(bool visible)
        {
            if (_isScreenVisible == visible) return;
            _isScreenVisible = visible;

            // Re-render on show: the queue may have moved on while we were hidden.
            if (visible) NotifyStateChanged();
        }

        // ── User intent handlers ───────────────────────────────────────────────────

        /// <summary>Player swiped right — accept the active card.</summary>
        public bool OnSwipeAccepted()
        {
            var card = _manager.ActiveCard;
            if (card == null) return false;
            if (!_manager.ResolveCard(card.CardId, accept: true)) return false;

            _audio.PlaySfx(SfxIds.CardAccept);
            _haptics.TriggerLight();
            return true;
        }

        /// <summary>Player swiped left — reject the active card.</summary>
        public bool OnSwipeRejected()
        {
            var card = _manager.ActiveCard;
            if (card == null) return false;
            if (!_manager.ResolveCard(card.CardId, accept: false)) return false;

            _audio.PlaySfx(SfxIds.CardReject);
            _haptics.TriggerLight();
            return true;
        }

        /// <summary>Player tapped the crystal refill button.</summary>
        public bool OnCrystalRefillRequested()
        {
            if (!_manager.RefillWithCrystals()) return false;

            _audio.PlaySfx(SfxIds.CrystalSpend);
            _haptics.TriggerLight();
            return true;
        }

        /// <summary>Player tapped the buy-up button for a specific characteristic.</summary>
        public bool OnBuyUpRequested(CharacteristicType type)
        {
            if (!_manager.BuyUpCharacteristic(type)) return false;

            _audio.PlaySfx(SfxIds.CrystalSpend);
            _haptics.TriggerLight();
            NotifyStateChanged();
            return true;
        }

        // ── Characteristic display data ────────────────────────────────────────────

        /// <summary>
        /// Build display data for one characteristic row.
        /// ProgressFraction resets to 0 on level-up (no floor marker, per design).
        /// </summary>
        public CharacteristicDisplayData GetCharacteristicDisplay(CharacteristicType type)
        {
            var   state    = _ledger.GetCharacteristic(type);
            float required = _ledger.PointsRequiredForLevel(state.Level);
            float progress = required > 0f
                ? (float)Math.Min(1.0, state.PointsIntoCurrentLevel / (double)required)
                : 0f;

            // Priced by the Manager, which is also what charges it — so the number shown
            // and the number taken cannot drift apart.
            int buyUpCost = _manager.GetBuyUpCost(type);

            // A zero cost means there is nothing to buy, not that it is free — keep the
            // button disabled so the View never offers a purchase the Manager would refuse.
            bool canAfford = buyUpCost > 0 && _ledger.Crystals >= buyUpCost;

            return new CharacteristicDisplayData(type, state.Level, progress, buyUpCost, canAfford);
        }

        // ── Private ────────────────────────────────────────────────────────────────

        private void HandleQueueChanged()
        {
            bool hasCardNow  = _manager.HasActiveCard;
            bool cardArrived = hasCardNow && !_hadActiveCard;
            _hadActiveCard   = hasCardNow;

            // GDD §3 wants a sound on every notable passive moment — but only one the
            // player is actually present for. See SetScreenVisible.
            if (cardArrived && _isScreenVisible)
                _audio.PlaySfx(SfxIds.CardArrive);

            NotifyStateChanged();
        }

        private void HandleCharacteristicLeveledUp(CharacteristicLeveledUp evt)
        {
            _audio.PlaySfx(SfxIds.LevelUp);
            _haptics.TriggerLight();
            OnCharacteristicLeveledUp?.Invoke(evt.CharacteristicType);
            NotifyStateChanged();
        }

        private void NotifyStateChanged() => OnStateChanged?.Invoke();

        /// <summary>
        /// Render a card's effects as the short, legible readout GDD §6 requires — the
        /// player should never be surprised by the outcome of a swipe.
        /// </summary>
        /// <remarks>
        /// Characteristic names come out as the raw enum today. That is the same
        /// placeholder the card's title and flavor use, and this is the single place
        /// localization will replace once String Tables exist (ARCHITECTURE.md §2).
        /// </remarks>
        private static string BuildEffectsSummary(LawCardEffect[] effects)
        {
            if (effects == null || effects.Length == 0) return string.Empty;

            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < effects.Length; i++)
            {
                if (i > 0) builder.Append('\n');
                var effect = effects[i];
                string sign = effect.Points >= 0f ? "+" : string.Empty;
                builder.Append(effect.Characteristic).Append(": ").Append(sign)
                       .Append(effect.Points.ToString("0"));
            }
            return builder.ToString();
        }
    }
}
