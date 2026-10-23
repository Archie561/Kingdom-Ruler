using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using KingdomRuler.Shared.Text;
using KingdomRuler.Systems.Audio;
using KingdomRuler.Systems.Events;
using KingdomRuler.Systems.Haptics;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Systems.Localization;
using KingdomRuler.Systems.Popups;
using KingdomRuler.Systems.Popups.Confirm;

namespace KingdomRuler.Modules.Laws.UI
{
    /// <summary>
    /// Mediates between <see cref="LawsManager"/> and <see cref="LawsScreenView"/>: turns model
    /// state into localized view data, carries player actions back, and owns the feel hooks
    /// (sound, haptics). Plain C#, no Unity UI types.
    /// </summary>
    public sealed class LawsPresenter : IDisposable
    {
        private static class Sfx
        {
            public const string CardAccept   = "sfx_laws_card_accept";
            public const string CardReject   = "sfx_laws_card_reject";
            public const string CardArrive   = "sfx_laws_card_arrive";
            public const string LevelUp      = "sfx_laws_level_up";
            public const string CrystalSpend = "sfx_laws_crystal_spend";
        }

        private readonly LawsManager            _manager;
        private readonly KingdomLedger          _ledger;
        private readonly EventBus               _eventBus;
        private readonly IAudioService          _audio;
        private readonly IHapticService         _haptics;
        private readonly ILocalizationService   _localization;
        private readonly CharacteristicRegistry _characteristics;
        private readonly PopupSystem            _popups;

        private bool _hadActiveCard;
        private bool _isScreenVisible;

        /// <summary>Anything on screen may have changed; re-render.</summary>
        public event Action StateChanged;

        /// <summary>A card is now shown where none was, however it got there.</summary>
        public event Action CardArrived;

        public event Action<CharacteristicType> CharacteristicLeveledUp;

        public LawsPresenter(
            LawsManager            manager,
            KingdomLedger          ledger,
            EventBus               eventBus,
            IAudioService          audio,
            IHapticService         haptics,
            ILocalizationService   localization,
            CharacteristicRegistry characteristics,
            PopupSystem            popups)
        {
            _manager      = manager      ?? throw new ArgumentNullException(nameof(manager));
            _ledger       = ledger       ?? throw new ArgumentNullException(nameof(ledger));
            _eventBus     = eventBus     ?? throw new ArgumentNullException(nameof(eventBus));
            _audio        = audio        ?? throw new ArgumentNullException(nameof(audio));
            _haptics      = haptics      ?? throw new ArgumentNullException(nameof(haptics));
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));

            // UnityEngine.Objects: `??` bypasses Unity's null check and would let a destroyed object through.
            if (characteristics == null) throw new ArgumentNullException(nameof(characteristics));
            if (popups == null)          throw new ArgumentNullException(nameof(popups));
            _characteristics = characteristics;
            _popups          = popups;

            _hadActiveCard = HasActiveCard;

            _manager.QueueChanged += HandleQueueChanged;
            _eventBus.Subscribe<CharacteristicLeveledUpEvent>(HandleCharacteristicLeveledUp);
            _localization.LocaleChanged += NotifyStateChanged;
        }

        public void Dispose()
        {
            _manager.QueueChanged -= HandleQueueChanged;
            _eventBus.Unsubscribe<CharacteristicLeveledUpEvent>(HandleCharacteristicLeveledUp);
            _localization.LocaleChanged -= NotifyStateChanged;
        }

        // ── State the view reads ──────────────────────────────────────────────────

        public bool  HasActiveCard        => _manager.ActiveCard != null;
        public int   HeldCardCount        => _manager.HeldCardCount;
        public int   MaxCardCount         => _manager.MaxCardCount;
        public bool  IsReplenishing       => _manager.ReplenishingCardCount > 0;
        public float SecondsUntilNextCard => _manager.SecondsUntilNextCard;
        public int   RefillCost           => _manager.RefillCost;
        public bool  CanAffordRefill      => _manager.CanAffordRefill;

        /// <summary>No card shown and none on the way: the "council is preparing decrees" state.</summary>
        public bool IsWaiting => !HasActiveCard && !IsReplenishing;

        public bool TryGetActiveCard(out LawCardViewData card)
        {
            var definition = _manager.ActiveCard;
            if (definition == null)
            {
                card = default;
                return false;
            }

            card = new LawCardViewData(
                _localization.Resolve(LawCardDefinition.StringTable, definition.TitleKey),
                _localization.Resolve(LawCardDefinition.StringTable, definition.FlavorKey));
            return true;
        }

        public CharacteristicDialViewData GetDial(CharacteristicType type)
        {
            var   state    = _ledger.GetCharacteristic(type);
            float required = _ledger.PointsRequiredForLevel(state.Level);
            float progress = required > 0f ? Mathf.Clamp01(state.PointsIntoCurrentLevel / required) : 0f;

            return new CharacteristicDialViewData(type, _characteristics.IconFor(type), state.Level, progress);
        }

        // ── Player actions ────────────────────────────────────────────────────────

        public bool ResolveCard(LawDecision decision)
        {
            if (!_manager.ResolveActiveCard(decision)) return false;

            _audio.PlaySfx(decision == LawDecision.Accept ? Sfx.CardAccept : Sfx.CardReject);
            _haptics.TriggerLight();
            return true;
        }

        /// <summary>Ask the player to confirm the refill; charge only if they do.</summary>
        public async UniTaskVoid RequestRefill()
        {
            if (!CanAffordRefill) return;

            // The popup re-reads its data while open, so the price and countdown stay live.
            var choice = await _popups.Create<ConfirmPopup>().Ask(
                () => new ConfirmPopupData(
                    _localization.Resolve(LawsUIText.StringTable, LawsUIText.RefillTitle),
                    DescribeRefill(),
                    canConfirm: CanAffordRefill),
                closeWhen: () => RefillCost <= 0);

            // The price is re-read by the manager now, not taken from what the popup showed.
            if (choice != ConfirmPopupChoice.Confirm || !_manager.TryRefill()) return;

            _audio.PlaySfx(Sfx.CrystalSpend);
            _haptics.TriggerLight();
        }

        // Buy-up is for the characteristic detail panel, which is not built yet.

        public int  BuyUpCost(CharacteristicType type)      => _manager.BuyUpCost(type);
        public bool CanAffordBuyUp(CharacteristicType type) => _manager.CanAffordBuyUp(type);

        public bool BuyUp(CharacteristicType type)
        {
            if (!_manager.TryBuyUp(type)) return false;

            _audio.PlaySfx(Sfx.CrystalSpend);
            _haptics.TriggerLight();
            NotifyStateChanged();
            return true;
        }

        /// <summary>Only sound depends on this: a card arriving on a hidden screen stays silent.</summary>
        public void SetScreenVisible(bool visible)
        {
            if (_isScreenVisible == visible) return;
            _isScreenVisible = visible;

            if (visible) NotifyStateChanged();
        }

        // ── Private ───────────────────────────────────────────────────────────────

        private string DescribeRefill() =>
            _localization.Resolve(LawsUIText.StringTable, LawsUIText.RefillBody,
                RefillCost,
                TimeFormat.MinutesSeconds((int)Math.Ceiling(_manager.SecondsUntilQueueFull)));

        private void HandleQueueChanged()
        {
            bool hasCard     = HasActiveCard;
            bool cardArrived = hasCard && !_hadActiveCard;
            _hadActiveCard   = hasCard;

            if (cardArrived && _isScreenVisible) _audio.PlaySfx(Sfx.CardArrive);

            NotifyStateChanged();

            // After the refresh, so the view already shows the card it animates.
            if (cardArrived) CardArrived?.Invoke();
        }

        private void HandleCharacteristicLeveledUp(CharacteristicLeveledUpEvent evt)
        {
            _audio.PlaySfx(Sfx.LevelUp);
            _haptics.TriggerLight();
            CharacteristicLeveledUp?.Invoke(evt.CharacteristicType);
            NotifyStateChanged();
        }

        private void NotifyStateChanged() => StateChanged?.Invoke();
    }
}
