using System;
using System.Collections.Generic;
using NUnit.Framework;
using KingdomRuler.Systems.Events;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Systems.Clock;
using KingdomRuler.Modules.Laws;
using KingdomRuler.Modules.Laws.Domain;
using UnityEngine;

namespace KingdomRuler.Tests.EditMode.Modules.Laws
{
    // ── Shared test helper (used by both domain and presenter tests) ───────────────
    public class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; } = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public void Advance(TimeSpan duration) => UtcNow += duration;

        public event Action<DateTime> Ticked;

        /// <summary>What GameClock does four times a second: tell every subscriber the time.</summary>
        public void Tick() => Ticked?.Invoke(UtcNow);
    }

    // ── LawsManagerTests ──────────────────────────────────────────────────────────

    [TestFixture]
    public sealed class LawsManagerTests
    {
        private FakeClock      _clock;
        private EventBus       _eventBus;
        private KingdomLedger  _ledger;
        private LawsConfig     _config;
        private LawsManager    _manager;
        private readonly List<ScriptableObject> _createdAssets = new();

        [SetUp]
        public void SetUp()
        {
            _clock    = new FakeClock();
            _eventBus = new EventBus();
            _ledger   = new KingdomLedger(_eventBus);

            _config = ScriptableObject.CreateInstance<LawsConfig>();
            _config.MaxHeldCards              = 8;
            _config.CardReplenishTimeSeconds  = 120f;
            _config.CrystalCostPerRefill      = 2;
            _createdAssets.Add(_config);

            _manager = new LawsManager(_ledger, _clock, _config);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var a in _createdAssets)
                if (a != null) UnityEngine.Object.DestroyImmediate(a);
            _createdAssets.Clear();
        }

        private LawCardDefinition CreateCard(string id,
            LawCardEffect[] accept = null, LawCardEffect[] reject = null)
        {
            var card = ScriptableObject.CreateInstance<LawCardDefinition>();
            card.CardId        = id;
            card.AcceptEffects = accept;
            card.RejectEffects = reject;
            _createdAssets.Add(card);
            return card;
        }

        /// Advance the clock and pump replenishment until the active slot is filled.
        private LawCardDefinition PushOneCardToActive(string id = "card_a",
            LawCardEffect[] accept = null, LawCardEffect[] reject = null)
        {
            var card = CreateCard(id, accept, reject);
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();
            _clock.Advance(TimeSpan.FromSeconds(120));
            _manager.AdvanceTo(_clock.UtcNow);
            return card;
        }

        // ── ResolveCard ───────────────────────────────────────────────────────────

        [Test]
        public void ResolveCard_Accept_AppliesAcceptEffects()
        {
            var effect = new LawCardEffect { Characteristic = CharacteristicType.Army, Points = 10f };
            var card   = PushOneCardToActive("card_a", accept: new[] { effect });

            float before = _ledger.GetCharacteristic(CharacteristicType.Army).PointsIntoCurrentLevel;
            bool result  = _manager.ResolveCard(card.CardId, accept: true);

            Assert.IsTrue(result);
            Assert.AreEqual(before + 10f,
                _ledger.GetCharacteristic(CharacteristicType.Army).PointsIntoCurrentLevel);
        }

        [Test]
        public void ResolveCard_Reject_AppliesRejectEffects()
        {
            var effect = new LawCardEffect { Characteristic = CharacteristicType.Infrastructure, Points = 5f };
            var card   = PushOneCardToActive("card_a", reject: new[] { effect });

            float before = _ledger.GetCharacteristic(CharacteristicType.Infrastructure).PointsIntoCurrentLevel;
            bool result  = _manager.ResolveCard(card.CardId, accept: false);

            Assert.IsTrue(result);
            Assert.AreEqual(before + 5f,
                _ledger.GetCharacteristic(CharacteristicType.Infrastructure).PointsIntoCurrentLevel);
        }

        [Test]
        public void ResolveCard_RemovesActiveCard()
        {
            var card = PushOneCardToActive();
            Assert.IsTrue(_manager.HasActiveCard);

            _manager.ResolveCard(card.CardId, true);

            Assert.IsFalse(_manager.HasActiveCard);
        }

        [Test]
        public void ResolveCard_WrongCardId_ReturnsFalse()
        {
            PushOneCardToActive();
            Assert.IsFalse(_manager.ResolveCard("wrong_id", true));
        }

        [Test]
        public void ResolveCard_NoActiveCard_ReturnsFalse()
        {
            Assert.IsFalse(_manager.ResolveCard("any_id", true));
        }

        [Test]
        public void ResolveCard_StartsReplenishmentTimer()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();

            // Advance enough for all 8 slots to fill.
            _clock.Advance(TimeSpan.FromSeconds(120 * 8));
            _manager.AdvanceTo(_clock.UtcNow);

            // 1 active, 7 ready, 0 replenishing.
            Assert.IsTrue(_manager.HasActiveCard);
            Assert.AreEqual(0, _manager.CardsReplenishing);

            _manager.ResolveCard(card.CardId, true);

            // Active slot consumed → new replenishment slot opened,
            // but a ready card immediately fills the active slot.
            Assert.IsTrue(_manager.HasActiveCard);  // ready card drawn
            Assert.AreEqual(1, _manager.CardsReplenishing); // one slot replenishing
        }

        [Test]
        public void ResolveCard_WhenNoReadySlots_ActiveSlotBecomesEmpty()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();

            // Only one replenishment cycle — 1 active, 0 ready, 7 replenishing.
            _clock.Advance(TimeSpan.FromSeconds(120));
            _manager.AdvanceTo(_clock.UtcNow);
            Assert.IsTrue(_manager.HasActiveCard);
            Assert.AreEqual(7, _manager.CardsReplenishing);

            _manager.ResolveCard(card.CardId, true);

            // No ready cards → active slot becomes empty, replenishing goes to 8.
            Assert.IsFalse(_manager.HasActiveCard);
            Assert.AreEqual(8, _manager.CardsReplenishing);
        }

        // ── AdvanceTo ──────────────────────────────────────────────────

        [Test]
        public void AdvanceTo_AddsActiveCardAfterEnoughTime()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();

            Assert.IsFalse(_manager.HasActiveCard);

            _clock.Advance(TimeSpan.FromSeconds(119));
            _manager.AdvanceTo(_clock.UtcNow);
            Assert.IsFalse(_manager.HasActiveCard);

            _clock.Advance(TimeSpan.FromSeconds(1));
            _manager.AdvanceTo(_clock.UtcNow);
            Assert.IsTrue(_manager.HasActiveCard);
        }

        [Test]
        public void AClockTick_BringsTheNextCard()
        {
            // The manager subscribes itself to the clock — nothing else has to remember to.
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();

            _clock.Advance(TimeSpan.FromSeconds(120));
            _clock.Tick();

            Assert.IsTrue(_manager.HasActiveCard);
        }

        /// <summary>
        /// Regression: a slot finishing while the active slot is already occupied is the
        /// COMMON case, and it changes two things the player can see — AvailableCardCount
        /// (the "N/8" readout) goes up and CardsReplenishing (the countdown) goes down. The
        /// manager used to publish only when a card was actually drawn, leaving the readout
        /// and timer stale until the player's next swipe.
        /// </summary>
        [Test]
        public void AdvanceTo_SlotCompletesWhileCardHeld_RaisesQueueChanged()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();

            // Fill the active slot first.
            _clock.Advance(TimeSpan.FromSeconds(120));
            _manager.AdvanceTo(_clock.UtcNow);
            Assert.IsTrue(_manager.HasActiveCard, "Precondition: a card is active.");
            Assert.AreEqual(1, _manager.AvailableCardCount, "Precondition: only the active card.");

            // Now subscribe and let one more slot mature. No card can be drawn — the
            // active slot is taken — but the readout must change and be announced.
            int published = 0;
            _manager.QueueChanged += () => published++;

            _clock.Advance(TimeSpan.FromSeconds(120));
            _manager.AdvanceTo(_clock.UtcNow);

            Assert.AreEqual(2, _manager.AvailableCardCount, "A slot matured into a held card.");
            Assert.IsTrue(_manager.HasActiveCard, "The active card is unchanged.");
            Assert.AreEqual(1, published,
                "A completed slot changes the readout and countdown, so it must publish.");
        }

        [Test]
        public void AdvanceTo_LastSlotCompletes_AnnouncesTimerStopped()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();

            // Draw the active card, then let 6 of the remaining 7 slots mature.
            _clock.Advance(TimeSpan.FromSeconds(120 * 7));
            _manager.AdvanceTo(_clock.UtcNow);
            Assert.AreEqual(1, _manager.CardsReplenishing, "Precondition: one slot still running.");

            int published = 0;
            _manager.QueueChanged += () => published++;

            _clock.Advance(TimeSpan.FromSeconds(120));
            _manager.AdvanceTo(_clock.UtcNow);

            Assert.AreEqual(0, _manager.CardsReplenishing,
                "The countdown has stopped — the timer label should hide.");
            Assert.AreEqual(1, published,
                "The View can only learn the countdown stopped if this is published.");
        }

        [Test]
        public void AdvanceTo_NothingMatured_DoesNotPublish()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();

            int published = 0;
            _manager.QueueChanged += () => published++;

            _clock.Advance(TimeSpan.FromSeconds(30)); // well short of one slot
            _manager.AdvanceTo(_clock.UtcNow);

            Assert.AreEqual(0, published, "No slot completed, so there is nothing to announce.");
        }

        [Test]
        public void AdvanceTo_MultipleElapsedCycles_OnlyDrawsOneActiveCard()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();

            // 3 slots worth of time — but only 1 active card can be shown.
            _clock.Advance(TimeSpan.FromSeconds(360));
            _manager.AdvanceTo(_clock.UtcNow);
            Assert.IsTrue(_manager.HasActiveCard);
            Assert.AreEqual(5, _manager.CardsReplenishing); // 8 - 1 active - 2 ready = 5 replenishing
        }

        [Test]
        public void AdvanceTo_CapsAtMaxReplenishingSlots()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();

            _clock.Advance(TimeSpan.FromSeconds(12000));
            _manager.AdvanceTo(_clock.UtcNow);

            // All 8 slots done: the player holds the full 8 (1 active + 7 behind it).
            Assert.IsTrue(_manager.HasActiveCard);
            Assert.AreEqual(0, _manager.CardsReplenishing);
            Assert.AreEqual(8, _manager.AvailableCardCount);
        }

        [Test]
        public void AdvanceTo_NoCardsReplenishing_DoesNothing()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();

            _clock.Advance(TimeSpan.FromSeconds(1200));
            _manager.AdvanceTo(_clock.UtcNow); // fills all slots

            bool hadCardBefore = _manager.HasActiveCard;
            int repBefore  = _manager.CardsReplenishing;

            _clock.Advance(TimeSpan.FromSeconds(120));
            _manager.AdvanceTo(_clock.UtcNow);

            Assert.AreEqual(hadCardBefore, _manager.HasActiveCard);
            Assert.AreEqual(repBefore,  _manager.CardsReplenishing);
        }

        // ── RefillWithCrystals ────────────────────────────────────────────────────

        [Test]
        public void RefillWithCrystals_SpendsCrystalsAndCancelsTimers()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool(); // 8 replenishing

            // Cost = 8 × 2 = 16
            _ledger.AddCrystals(20);
            bool result = _manager.RefillWithCrystals();

            Assert.IsTrue(result);
            Assert.AreEqual(0,  _manager.CardsReplenishing); // all timers cancelled
            Assert.IsTrue(_manager.HasActiveCard);          // 1 active card drawn
            Assert.AreEqual(4,  _ledger.Crystals);           // 20 - 16
        }

        [Test]
        public void RefillWithCrystals_FailsIfNotEnoughCrystals()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();

            _ledger.AddCrystals(1);
            bool result = _manager.RefillWithCrystals();

            Assert.IsFalse(result);
            Assert.AreEqual(8, _manager.CardsReplenishing);
            Assert.AreEqual(1, _ledger.Crystals);
        }

        [Test]
        public void RefillWithCrystals_WhenNoSlotsReplenishing_ReturnsFalse()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();

            _clock.Advance(TimeSpan.FromSeconds(1200));
            _manager.AdvanceTo(_clock.UtcNow); // all slots filled

            _ledger.AddCrystals(100);
            Assert.IsFalse(_manager.RefillWithCrystals());
            Assert.AreEqual(100, _ledger.Crystals);
        }

        // ── BuyUpCharacteristic ───────────────────────────────────────────────────

        [Test]
        public void BuyUpCharacteristic_SpendsCrystalsAndLevelsUp()
        {
            _ledger.AddCrystals(100);
            int levelBefore = _ledger.GetCharacteristic(CharacteristicType.Army).Level;

            bool result = _manager.BuyUpCharacteristic(CharacteristicType.Army);

            Assert.IsTrue(result);
            Assert.AreEqual(levelBefore + 1, _ledger.GetCharacteristic(CharacteristicType.Army).Level);
            Assert.Less(_ledger.Crystals, 100);
        }

        [Test]
        public void BuyUpCharacteristic_FailsIfNotEnoughCrystals()
        {
            int levelBefore = _ledger.GetCharacteristic(CharacteristicType.Army).Level;
            bool result     = _manager.BuyUpCharacteristic(CharacteristicType.Army);

            Assert.IsFalse(result);
            Assert.AreEqual(levelBefore, _ledger.GetCharacteristic(CharacteristicType.Army).Level);
        }

        // ── GetSecondsUntilNextCard ───────────────────────────────────────────────

        [Test]
        public void GetSecondsUntilNextCard_ReturnsCorrectRemainingTime()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();

            _clock.Advance(TimeSpan.FromSeconds(20));
            _manager.AdvanceTo(_clock.UtcNow);

            Assert.AreEqual(100f, _manager.GetSecondsUntilNextCard(), 0.01f);
        }

        [Test]
        public void GetSecondsUntilNextCard_ReturnsZeroWhenNoReplenishment()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool();

            _clock.Advance(TimeSpan.FromSeconds(1200));
            _manager.AdvanceTo(_clock.UtcNow);

            Assert.AreEqual(0f, _manager.GetSecondsUntilNextCard());
        }

        // ── Shuffle-bag tests ─────────────────────────────────────────────────────

        [Test]
        public void ShuffleBag_AllCardsDrawnBeforeRepeat_OneFullCycle()
        {
            // Five distinct cards — each must appear exactly once per cycle.
            int n = 5;
            var cards = new LawCardDefinition[n];
            for (int i = 0; i < n; i++)
                cards[i] = CreateCard($"card_{i}");
            _config.AllCards = cards;
            _manager.InitializeCardPool();

            var seen = new HashSet<string>();
            for (int i = 0; i < n; i++)
            {
                // Advance one slot per iteration to get cards one-by-one.
                _clock.Advance(TimeSpan.FromSeconds(120));
                _manager.AdvanceTo(_clock.UtcNow);

                // Resolve the active card to make room for the next.
                var active = _manager.ActiveCard;
                if (active != null)
                {
                    Assert.IsFalse(seen.Contains(active.CardId),
                        $"Card '{active.CardId}' appeared twice in the same cycle.");
                    seen.Add(active.CardId);
                    _manager.ResolveCard(active.CardId, true);
                }
            }

            Assert.AreEqual(n, seen.Count, "Not all cards appeared in the first cycle.");
        }

        [Test]
        public void ShuffleBag_NoBackToBackOnCycleBoundary()
        {
            // Two cards: exhausting the full cycle forces a reshuffle where the
            // first card of the new cycle is guaranteed ≠ the last of the old.
            var cardA = CreateCard("A");
            var cardB = CreateCard("B");
            _config.AllCards = new[] { cardA, cardB };
            _manager.InitializeCardPool();

            // Draw all 2 cards in the first cycle.
            string lastDrawn = null;
            for (int i = 0; i < 2; i++)
            {
                _clock.Advance(TimeSpan.FromSeconds(120));
                _manager.AdvanceTo(_clock.UtcNow);
                var active = _manager.ActiveCard;
                if (active != null)
                {
                    lastDrawn = active.CardId;
                    _manager.ResolveCard(active.CardId, true);
                }
            }

            Assert.IsNotNull(lastDrawn, "No cards were drawn.");

            // Draw the first card of the new cycle (cycle 2).
            _clock.Advance(TimeSpan.FromSeconds(120));
            _manager.AdvanceTo(_clock.UtcNow);
            var firstOfNewCycle = _manager.ActiveCard;

            Assert.IsNotNull(firstOfNewCycle, "First card of new cycle was null.");
            Assert.AreNotEqual(lastDrawn, firstOfNewCycle.CardId,
                $"Back-to-back repeat: '{lastDrawn}' was last of cycle 1 AND first of cycle 2.");
        }

        [Test]
        public void ShuffleBag_CycleRestarts_WhenDeckExhausted()
        {
            // 3-card set. Exhaust full cycle then verify we can draw again.
            int n = 3;
            var cards = new LawCardDefinition[n];
            for (int i = 0; i < n; i++) cards[i] = CreateCard($"c_{i}");
            _config.AllCards = cards;
            _manager.InitializeCardPool();

            // Exhaust first cycle.
            for (int i = 0; i < n; i++)
            {
                _clock.Advance(TimeSpan.FromSeconds(120));
                _manager.AdvanceTo(_clock.UtcNow);
                var active = _manager.ActiveCard;
                if (active != null) _manager.ResolveCard(active.CardId, true);
            }

            // Second cycle: can still draw.
            _clock.Advance(TimeSpan.FromSeconds(120));
            _manager.AdvanceTo(_clock.UtcNow);
            Assert.IsNotNull(_manager.ActiveCard,
                "No card available after deck exhaustion and reshuffle.");
        }

        // ── Save / Load ───────────────────────────────────────────────────────────

        [Test]
        public void LoadFromDto_RestoresActiveCard()
        {
            var card = PushOneCardToActive();
            var dto  = _manager.ToDto();

            var manager2 = new LawsManager(_ledger, new FakeClock(), _config);
            manager2.LoadFromDto(dto);

            Assert.IsTrue(manager2.HasActiveCard);
            Assert.AreEqual(card.CardId, manager2.ActiveCard.CardId);
        }

        [Test]
        public void LoadFromDto_ProcessesOfflineCatchUp()
        {
            var card = CreateCard("card");
            _config.AllCards = new[] { card };
            _manager.InitializeCardPool(); // 8 replenishing

            var dto = _manager.ToDto();

            // Simulate offline time: 2 card replenishments elapsed.
            var futureClock = new FakeClock();
            futureClock.Advance(TimeSpan.FromSeconds(240));

            var manager2 = new LawsManager(_ledger, futureClock, _config);
            manager2.LoadFromDto(dto);

            // 2 slots should have completed offline → 1 active, 1 ready, 6 replenishing.
            Assert.IsTrue(manager2.HasActiveCard);
            Assert.AreEqual(6, manager2.CardsReplenishing);
        }

        [Test]
        public void LoadFromDto_RestoresRemainingDeck()
        {
            var cardA = CreateCard("A");
            var cardB = CreateCard("B");
            var cardC = CreateCard("C");
            _config.AllCards = new[] { cardA, cardB, cardC };
            _manager.InitializeCardPool();

            // Draw one card.
            _clock.Advance(TimeSpan.FromSeconds(120));
            _manager.AdvanceTo(_clock.UtcNow);
            _manager.ResolveCard(_manager.ActiveCard.CardId, true);

            var dto = _manager.ToDto();
            Assert.AreEqual(2, dto.RemainingDeckCardIds.Count,
                "Two cards should remain in deck after one draw.");

            var manager2 = new LawsManager(_ledger, _clock, _config);
            manager2.LoadFromDto(dto);

            // Remaining deck count should match.
            var dto2 = manager2.ToDto();
            Assert.AreEqual(dto.RemainingDeckCardIds.Count, dto2.RemainingDeckCardIds.Count);
        }
    }
}
