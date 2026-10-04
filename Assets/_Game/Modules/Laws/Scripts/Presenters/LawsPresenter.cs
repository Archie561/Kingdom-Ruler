using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using KingdomRuler.Core;
using KingdomRuler.Shared.Ledger;
using KingdomRuler.Shared.Popups;
using KingdomRuler.Shared.Popups.Confirm;
using KingdomRuler.Shared.Text;
using KingdomRuler.Shared.Services;

namespace KingdomRuler.Modules.Laws.Presenters
{
    /// <summary>
    /// Mediates between LawsManager (Model) and LawsView (View).
    /// Plain C# — no MonoBehaviour, no DOTween, fully unit-testable.
    ///
    /// Everything the View draws is prepared here as plain values
    /// (<see cref="LawCardDisplayData"/>, <see cref="CharacteristicDisplayData"/>), so the
    /// View never touches a LawCardDefinition asset or a Domain type.
    ///
    /// Flow:
    ///   1. LawsView subscribes to OnStateChanged / OnCharacteristicLeveledUp.
    ///   2. LawsManager raises QueueChanged → Presenter fires OnStateChanged.
    ///   3. LawsView forwards user interactions via OnSwipeAccepted / etc.
    ///   4. LawsView reports visibility via SetScreenVisible, which gates audio only.
    /// </summary>
    public sealed class LawsPresenter : IDisposable
    {
        // ── String Tables ──────────────────────────────────────────────────────────
        // No table names are declared here, deliberately. Each one is owned by the type whose
        // text it holds — LawCardDefinition for card content, CharacteristicRegistry for
        // characteristic names — together with the key derivation, so the Presenter and the
        // Editor validators resolve through one definition instead of matching copies.
        // See ARCHITECTURE.md §2.
        //
        // LawsUITable appears nowhere in code: this screen's chrome is resolved by
        // LocalizeStringEvent components in the prefab.

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
        private readonly ILocalizationService   _localization;
        private readonly CharacteristicRegistry _characteristics;
        private readonly PopupManager           _popups;

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

        /// <summary>
        /// Fired when a card becomes the active one having not been there before — however it
        /// arrived: a timer maturing, a crystal refill, a fresh game.
        /// </summary>
        /// <remarks>
        /// The View animates the card in from this rather than from whichever button was
        /// pressed. The button knows why it was clicked; only the Presenter knows whether a
        /// card actually turned up, and those two stopped being the same thing the moment the
        /// refill button started opening a confirmation instead of refilling.
        /// </remarks>
        public event Action OnCardArrived;

        // ── Read-only state the View polls ────────────────────────────────────────

        /// <summary>Whether a card is currently being presented to the player.</summary>
        public bool HasActiveCard => _manager.HasActiveCard;

        /// <summary>
        /// Build the render-ready data for the active card. Returns false when there is
        /// none, in which case <paramref name="display"/> is meaningless.
        /// </summary>
        /// <remarks>
        /// The View deliberately never sees the LawCardDefinition asset — everything it
        /// draws is resolved into localized strings here, keyed off the card's id.
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
                _localization.Resolve(LawCardDefinition.StringTable, card.TitleKey),
                _localization.Resolve(LawCardDefinition.StringTable, card.FlavorKey));
            return true;
        }

        /// <summary>
        /// Cards the player holds, the active one included — the "N" in the "N/8" readout.
        /// </summary>
        public int AvailableCardCount => _manager.AvailableCardCount;

        /// <summary>The cap — the "8" in the "N/8" readout.</summary>
        public int MaxCardCount => _manager.MaxCardCount;

        /// <summary>True when no card is shown and nothing is replenishing.</summary>
        public bool IsWaiting => !_manager.HasActiveCard && _manager.CardsReplenishing == 0;

        /// <summary>True when at least one slot is counting down.</summary>
        public bool IsReplenishing => _manager.CardsReplenishing > 0;

        /// <summary>Seconds until the next replenishment slot fires.</summary>
        public float SecondsUntilNextCard => _manager.GetSecondsUntilNextCard();

        /// <summary>Seconds until the queue refills itself completely — what refilling skips.</summary>
        public float SecondsUntilAllCards => _manager.GetSecondsUntilAllCards();

        /// <summary>
        /// Crystal cost to instant-complete all pending replenishment timers.
        /// Read from the Manager rather than recomputed, so the price shown is by
        /// construction the price charged.
        /// </summary>
        public int RefillCost => _manager.RefillCost;

        /// <summary>
        /// True when there is something to refill AND the player can pay for it.
        /// A zero cost means nothing is pending, not that it is free — same gate the
        /// buy-up button uses.
        /// </summary>
        public bool CanAffordRefill => RefillCost > 0 && _ledger.Crystals >= RefillCost;

        // ── Constructor ───────────────────────────────────────────────────────────

        public LawsPresenter(
            LawsManager    manager,
            KingdomLedger  ledger,
            EventBus       eventBus,
            IAudioService  audio,
            IHapticService haptics,
            ILocalizationService   localization,
            CharacteristicRegistry characteristics,
            PopupManager           popups)
        {
            _manager  = manager  ?? throw new ArgumentNullException(nameof(manager));
            _ledger   = ledger   ?? throw new ArgumentNullException(nameof(ledger));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _audio    = audio    ?? throw new ArgumentNullException(nameof(audio));
            _haptics  = haptics  ?? throw new ArgumentNullException(nameof(haptics));
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));

            // Explicit == null rather than ??, because CharacteristicRegistry is a
            // UnityEngine.Object: ?? skips Unity's overloaded equality, so a destroyed asset
            // would pass this check and then fail on first use.
            if (characteristics == null) throw new ArgumentNullException(nameof(characteristics));
            _characteristics = characteristics;

            // Explicit == null: PopupManager is a UnityEngine.Object, and ?? skips Unity's
            // overloaded equality, so a destroyed manager would slip through.
            if (popups == null) throw new ArgumentNullException(nameof(popups));
            _popups = popups;

            _hadActiveCard = _manager.HasActiveCard;

            // CharacteristicLeveledUp is a cross-module ledger event, so it comes off the
            // bus. The queue change is Laws-internal, so it comes straight off the Manager
            // this Presenter already holds (ARCHITECTURE.md §4.2).
            _eventBus.Subscribe<CharacteristicLeveledUp>(HandleCharacteristicLeveledUp);
            _manager.QueueChanged += HandleQueueChanged;

            // Everything handed to the View is resolved text, so a locale change — or
            // localization finishing its async startup — invalidates all of it. The
            // LocalizeStringEvent components refresh themselves; these strings do not.
            _localization.LocaleChanged += NotifyStateChanged;
        }

        public void Dispose()
        {
            _eventBus.Unsubscribe<CharacteristicLeveledUp>(HandleCharacteristicLeveledUp);
            _manager.QueueChanged -= HandleQueueChanged;
            _localization.LocaleChanged -= NotifyStateChanged;
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

        /// <summary>
        /// Player tapped refill. Opens the confirmation and, only if the player says yes,
        /// charges.
        /// </summary>
        /// <remarks>
        /// <para>Crystals are a premium currency, so nothing is spent on the tap. The flow
        /// reads top to bottom precisely because it is awaited — the popup being open is just
        /// a pause in the middle of this method.</para>
        ///
        /// <para>The popup re-reads its data while it is open, so the countdown ticks and the
        /// price follows the queue without anything here pushing updates into it.</para>
        ///
        /// <para>Returns <c>UniTaskVoid</c> rather than <c>async void</c>: an exception in the
        /// latter is unobservable and can take the app down.</para>
        /// </remarks>
        public async UniTaskVoid OnCrystalRefillRequested()
        {
            if (!CanAffordRefill) return;

            var choice = await _popups.Create<ConfirmPopup>().Ask(
                () => new ConfirmPopupData(
                    _localization.Resolve(LawsUIText.StringTable, LawsUIText.RefillTitle),
                    DescribeRefill(),
                    canConfirm: CanAffordRefill),
                closeWhen: () => RefillCost <= 0);   // every law came back on its own meanwhile

            if (choice == ConfirmPopupChoice.Confirm) CompleteRefill();
        }

        private string DescribeRefill() =>
            _localization.Resolve(LawsUIText.StringTable, LawsUIText.RefillBody,
                RefillCost,
                TimeFormat.MinutesSeconds((int)Math.Ceiling(SecondsUntilAllCards)));

        /// <summary>
        /// Charge and refill, after confirmation.
        /// </summary>
        /// <remarks>
        /// Goes through the Manager rather than a price captured when the dialog opened, so
        /// the amount taken is the amount that is current at this instant — the queue may have
        /// refilled itself while the player was reading.
        /// </remarks>
        private void CompleteRefill()
        {
            if (!_manager.RefillWithCrystals()) return;

            _audio.PlaySfx(SfxIds.CrystalSpend);
            _haptics.TriggerLight();
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

            return new CharacteristicDisplayData(
                type,
                ResolveCharacteristicName(type),
                _characteristics.IconFor(type),
                state.Level, progress, buyUpCost, canAfford);
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

            // After the refresh: the View has populated and shown the new card by now, so
            // the animation has something to play on.
            if (cardArrived) OnCardArrived?.Invoke();
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
        /// Characteristic names come from the shared table via
        /// <see cref="CharacteristicRegistry"/>, not from a Laws-owned key.
        /// </summary>
        /// <remarks>
        /// Laws is simply the first consumer: Cities needs the same names for purchase
        /// requirements and Random Occurrences for outcome text. Keeping both the table and
        /// the key derivation on the registry means those modules inject one object and get
        /// the identical strings, instead of each re-deriving a convention that would then
        /// drift the first time it changed (<c>ARCHITECTURE.md</c> §4.3).
        /// </remarks>
        private string ResolveCharacteristicName(CharacteristicType type) =>
            _localization.Resolve(
                CharacteristicDefinition.StringTable, _characteristics.NameKeyFor(type));
    }
}
