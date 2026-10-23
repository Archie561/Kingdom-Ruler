using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using KingdomRuler.Systems.Events;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Modules.Laws;

namespace KingdomRuler.Tests.EditMode.Modules.Laws
{
    /// <summary>The manager's own job: Ledger transactions, the clock, change events and the save.</summary>
    [TestFixture]
    public sealed class LawsManagerTests
    {
        private FakeClock     _clock;
        private KingdomLedger _ledger;
        private LawsConfig    _config;
        private LawsManager   _manager;
        private readonly List<ScriptableObject> _created = new();

        [SetUp]
        public void SetUp()
        {
            _clock  = new FakeClock();
            _ledger = new KingdomLedger(new EventBus());

            _config = ScriptableObject.CreateInstance<LawsConfig>();
            _config.MaxHeldCards             = 8;
            _config.CardReplenishTimeSeconds = 120f;
            _config.CrystalCostPerRefill     = 2;
            _config.CrystalBuyUpDivisor      = 20f;
            _config.AllCards                 = new[] { Card("card") };
            _created.Add(_config);

            _manager = new LawsManager(_ledger, _clock, _config, new System.Random(1));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in _created)
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            _created.Clear();
        }

        private LawCardDefinition Card(string id, LawCardEffect[] accept = null, LawCardEffect[] reject = null)
        {
            var card = ScriptableObject.CreateInstance<LawCardDefinition>();
            card.CardId        = id;
            card.AcceptEffects = accept;
            card.RejectEffects = reject;
            _created.Add(card);
            return card;
        }

        private void Wait(int intervals)
        {
            _clock.Advance(TimeSpan.FromSeconds(120 * intervals));
            _manager.AdvanceTo(_clock.UtcNow);
        }

        /// <summary>Start a game with one card and wait until it is shown.</summary>
        private void ShowCard(LawCardEffect[] accept = null, LawCardEffect[] reject = null)
        {
            _config.AllCards = new[] { Card("card", accept, reject) };
            _manager.InitializeNewGame();
            Wait(1);
        }

        private float Points(CharacteristicType type) => _ledger.GetCharacteristic(type).PointsIntoCurrentLevel;

        // ── Resolving a card: effects ─────────────────────────────────────────────

        [Test]
        public void Accept_AppliesTheAcceptEffects()
        {
            ShowCard(accept: new[] { new LawCardEffect(CharacteristicType.Army, 10f) },
                     reject: new[] { new LawCardEffect(CharacteristicType.Army, 99f) });

            Assert.IsTrue(_manager.ResolveActiveCard(LawDecision.Accept));
            Assert.AreEqual(10f, Points(CharacteristicType.Army));
        }

        [Test]
        public void Reject_AppliesTheRejectEffects()
        {
            ShowCard(accept: new[] { new LawCardEffect(CharacteristicType.Science, 99f) },
                     reject: new[] { new LawCardEffect(CharacteristicType.Science, 5f) });

            Assert.IsTrue(_manager.ResolveActiveCard(LawDecision.Reject));
            Assert.AreEqual(5f, Points(CharacteristicType.Science));
        }

        /// <summary>Cards author one signed number; getting the sign backwards would invert every negative law.</summary>
        [Test]
        public void ANegativeEffect_RemovesProgress()
        {
            _ledger.AddCharacteristicPoints(CharacteristicType.Army, 60f);
            ShowCard(accept: new[] { new LawCardEffect(CharacteristicType.Army, -25f) });

            _manager.ResolveActiveCard(LawDecision.Accept);

            Assert.AreEqual(35f, Points(CharacteristicType.Army));
        }

        [Test]
        public void ANegativeEffect_NeverDropsALevel()
        {
            _ledger.AddCharacteristicPoints(CharacteristicType.Army, 150f);   // → level 2
            ShowCard(accept: new[] { new LawCardEffect(CharacteristicType.Army, -9999f) });

            _manager.ResolveActiveCard(LawDecision.Accept);

            Assert.AreEqual(2, _ledger.GetCharacteristic(CharacteristicType.Army).Level);
            Assert.AreEqual(0f, Points(CharacteristicType.Army));
        }

        [Test]
        public void SeveralEffects_EachReachItsOwnCharacteristic_AndRepeatsAccumulate()
        {
            ShowCard(accept: new[]
            {
                new LawCardEffect(CharacteristicType.Army, 10f),
                new LawCardEffect(CharacteristicType.Welfare, 30f),
                new LawCardEffect(CharacteristicType.Army, 15f)
            });

            _manager.ResolveActiveCard(LawDecision.Accept);

            Assert.AreEqual(25f, Points(CharacteristicType.Army));
            Assert.AreEqual(30f, Points(CharacteristicType.Welfare));
        }

        [Test]
        public void ACardWithNoEffects_ResolvesWithoutChangingAnything()
        {
            ShowCard();

            Assert.IsTrue(_manager.ResolveActiveCard(LawDecision.Reject));
            Assert.AreEqual(0f, Points(CharacteristicType.Army));
        }

        [Test]
        public void Resolve_WithNoCardShown_ReturnsFalse()
        {
            Assert.IsFalse(_manager.ResolveActiveCard(LawDecision.Accept));
        }

        [Test]
        public void Resolve_RaisesQueueChanged()
        {
            ShowCard();
            int raised = 0;
            _manager.QueueChanged += () => raised++;

            _manager.ResolveActiveCard(LawDecision.Accept);

            Assert.AreEqual(1, raised);
        }

        // ── The clock ─────────────────────────────────────────────────────────────

        [Test]
        public void AClockTick_BringsTheNextCard()
        {
            _manager.InitializeNewGame();
            _clock.Advance(TimeSpan.FromSeconds(120));

            _clock.Tick();

            Assert.IsNotNull(_manager.ActiveCard);
        }

        [Test]
        public void AMaturedSlot_RaisesQueueChanged_EvenWhenACardIsAlreadyShown()
        {
            ShowCard();
            int raised = 0;
            _manager.QueueChanged += () => raised++;

            Wait(1);

            Assert.AreEqual(2, _manager.HeldCardCount);
            Assert.AreEqual(1, raised, "The held count and countdown changed, so the view must hear of it.");
        }

        [Test]
        public void NothingMatured_RaisesNothing()
        {
            _manager.InitializeNewGame();
            int raised = 0;
            _manager.QueueChanged += () => raised++;

            _clock.Advance(TimeSpan.FromSeconds(30));
            _manager.AdvanceTo(_clock.UtcNow);

            Assert.AreEqual(0, raised);
        }

        [Test]
        public void SecondsUntilNextCard_ReadsTheClock()
        {
            _manager.InitializeNewGame();
            _clock.Advance(TimeSpan.FromSeconds(20));

            Assert.AreEqual(100f, _manager.SecondsUntilNextCard, 0.01f);
            Assert.AreEqual(120f * 8 - 20f, _manager.SecondsUntilQueueFull, 0.01f);
        }

        // ── Refill ────────────────────────────────────────────────────────────────

        [Test]
        public void RefillCost_IsTheCostPerCardForEveryReplenishingCard()
        {
            _manager.InitializeNewGame();
            Assert.AreEqual(16, _manager.RefillCost);
        }

        [Test]
        public void TryRefill_ChargesTheQuotedPriceAndFillsTheHand()
        {
            _manager.InitializeNewGame();
            _ledger.AddCrystals(20);
            int quoted = _manager.RefillCost;

            Assert.IsTrue(_manager.TryRefill());

            Assert.AreEqual(20 - quoted, _ledger.Crystals);
            Assert.AreEqual(0, _manager.ReplenishingCardCount);
            Assert.IsNotNull(_manager.ActiveCard);
        }

        [Test]
        public void TryRefill_WithoutEnoughCrystals_ChangesNothing()
        {
            _manager.InitializeNewGame();
            _ledger.AddCrystals(1);

            Assert.IsFalse(_manager.CanAffordRefill);
            Assert.IsFalse(_manager.TryRefill());
            Assert.AreEqual(8, _manager.ReplenishingCardCount);
            Assert.AreEqual(1, _ledger.Crystals);
        }

        [Test]
        public void TryRefill_WithAFullHand_IsRefusedAndFree()
        {
            _manager.InitializeNewGame();
            Wait(8);
            _ledger.AddCrystals(100);

            Assert.AreEqual(0, _manager.RefillCost);
            Assert.IsFalse(_manager.CanAffordRefill);
            Assert.IsFalse(_manager.TryRefill());
            Assert.AreEqual(100, _ledger.Crystals);
        }

        // ── Buy-up ────────────────────────────────────────────────────────────────

        // Level 1 needs 100 points (GDD §6).
        [TestCase(0f,     20f, 5)]   // 100 / 20
        [TestCase(5f,     20f, 5)]   // 95 / 20 rounds up
        [TestCase(0f,     30f, 4)]   // 100 / 30 rounds up
        [TestCase(99f,    20f, 1)]   // a tiny remainder still costs the minimum of 1
        [TestCase(99.99f, 20f, 1)]
        public void BuyUpCost_IsTheRemainderOverTheDivisor_RoundedUp(float pointsHeld, float divisor, int expected)
        {
            _config.CrystalBuyUpDivisor = divisor;
            _ledger.AddCharacteristicPoints(CharacteristicType.Army, pointsHeld);

            Assert.AreEqual(expected, _manager.BuyUpCost(CharacteristicType.Army));
        }

        [Test]
        public void BuyUpCost_WithANonPositiveDivisor_Throws()
        {
            _config.CrystalBuyUpDivisor = 0f;
            Assert.Throws<InvalidOperationException>(() => _manager.BuyUpCost(CharacteristicType.Army));
        }

        [Test]
        public void TryBuyUp_ChargesTheQuotedPriceAndLevelsUp_AtEveryLevel()
        {
            _ledger.AddCrystals(100_000);

            for (int level = 1; level <= 6; level++)
            {
                int quoted = _manager.BuyUpCost(CharacteristicType.Science);
                int before = _ledger.Crystals;

                Assert.IsTrue(_manager.TryBuyUp(CharacteristicType.Science));
                Assert.AreEqual(quoted, before - _ledger.Crystals, $"Price mismatch at level {level}.");
                Assert.AreEqual(level + 1, _ledger.GetCharacteristic(CharacteristicType.Science).Level);
            }
        }

        [Test]
        public void TryBuyUp_WithoutEnoughCrystals_ChangesNothing()
        {
            _ledger.AddCrystals(4);   // costs 5

            Assert.IsFalse(_manager.CanAffordBuyUp(CharacteristicType.Army));
            Assert.IsFalse(_manager.TryBuyUp(CharacteristicType.Army));
            Assert.AreEqual(1, _ledger.GetCharacteristic(CharacteristicType.Army).Level);
            Assert.AreEqual(4, _ledger.Crystals);
        }

        [Test]
        public void CanAffordBuyUp_WithExactlyThePrice_IsTrue()
        {
            _ledger.AddCrystals(5);
            Assert.IsTrue(_manager.CanAffordBuyUp(CharacteristicType.Army));
        }

        // ── Save / load ───────────────────────────────────────────────────────────

        [Test]
        public void ARoundTrip_RestoresTheShownCardDeckAndTimer()
        {
            _config.AllCards = new[] { Card("A"), Card("B"), Card("C") };
            _manager.InitializeNewGame();
            Wait(1);
            var dto = _manager.ToDto();

            var restored = new LawsManager(_ledger, _clock, _config);
            restored.LoadFromDto(dto);
            var dto2 = restored.ToDto();

            Assert.AreEqual(dto.ActiveCardId, dto2.ActiveCardId);
            Assert.AreEqual(dto.LastDrawnCardId, dto2.LastDrawnCardId);
            CollectionAssert.AreEqual(dto.RemainingDeckCardIds, dto2.RemainingDeckCardIds);
            Assert.AreEqual(dto.CardsReplenishing, dto2.CardsReplenishing);
            Assert.AreEqual(dto.NextReplenishDueUtc, dto2.NextReplenishDueUtc);
        }

        [Test]
        public void Load_SettlesTheTimeTheAppWasClosed()
        {
            _manager.InitializeNewGame();
            var dto = _manager.ToDto();

            var later = new FakeClock();
            later.Advance(TimeSpan.FromSeconds(240));
            var restored = new LawsManager(_ledger, later, _config);
            restored.LoadFromDto(dto);

            Assert.IsNotNull(restored.ActiveCard);
            Assert.AreEqual(6, restored.ReplenishingCardCount);
        }

        [Test]
        public void Load_WithAnUnreadableDeadline_RestartsTheCountdownInsteadOfDroppingTheQueue()
        {
            _manager.InitializeNewGame();
            var dto = _manager.ToDto();
            dto.NextReplenishDueUtc = "not a date";

            var restored = new LawsManager(_ledger, _clock, _config);
            restored.LoadFromDto(dto);

            Assert.AreEqual(8, restored.ReplenishingCardCount);
            Assert.AreEqual(120f, restored.SecondsUntilNextCard, 0.01f);
        }

        /// <summary>A save written with the device clock set forward must not stall the queue once it is set back.</summary>
        [Test]
        public void Load_WithADeadlineFarInTheFuture_WaitsAtMostOneInterval()
        {
            _manager.InitializeNewGame();
            var dto = _manager.ToDto();
            dto.NextReplenishDueUtc = _clock.UtcNow.AddDays(3).ToString("O");

            var restored = new LawsManager(_ledger, _clock, _config);
            restored.LoadFromDto(dto);

            Assert.AreEqual(120f, restored.SecondsUntilNextCard, 0.01f);
        }

        [Test]
        public void Load_WithNoSave_StartsANewGame()
        {
            _manager.LoadFromDto(null);
            Assert.AreEqual(8, _manager.ReplenishingCardCount);
        }

        [Test]
        public void ANewGameWithNoCards_Warns()
        {
            _config.AllCards = null;
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no cards"));
            _manager.InitializeNewGame();
        }
    }
}
