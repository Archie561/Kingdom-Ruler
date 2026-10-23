using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using KingdomRuler.Modules.Laws;

namespace KingdomRuler.Tests.EditMode.Modules.Laws
{
    /// <summary>The hand of cards on its own: no Ledger, no clock — just handed the time.</summary>
    [TestFixture]
    public sealed class LawQueueTests
    {
        private const float Interval = 120f;
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly List<ScriptableObject> _created = new();
        private LawsConfig _config;
        private LawQueue   _queue;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<LawsConfig>();
            _config.MaxHeldCards             = 8;
            _config.CardReplenishTimeSeconds = Interval;
            _config.AllCards                 = new[] { Card("a"), Card("b"), Card("c") };
            _created.Add(_config);

            _queue = new LawQueue(_config, new System.Random(1));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in _created)
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            _created.Clear();
        }

        private LawCardDefinition Card(string id)
        {
            var card = ScriptableObject.CreateInstance<LawCardDefinition>();
            card.CardId = id;
            _created.Add(card);
            return card;
        }

        private static DateTime After(int intervals, int extraSeconds = 0) =>
            T0.AddSeconds(Interval * intervals + extraSeconds);

        // ── Fresh start ───────────────────────────────────────────────────────────

        [Test]
        public void StartEmpty_HoldsNothingAndReplenishesEverySlot()
        {
            _queue.StartEmpty(T0);

            Assert.IsNull(_queue.ActiveCard);
            Assert.AreEqual(0, _queue.HeldCount);
            Assert.AreEqual(8, _queue.ReplenishingCount);
        }

        // ── Advance ───────────────────────────────────────────────────────────────

        [Test]
        public void Advance_ShowsACardOnceTheFirstSlotMatures()
        {
            _queue.StartEmpty(T0);

            Assert.IsFalse(_queue.Advance(After(1, -1)));
            Assert.IsNull(_queue.ActiveCard);

            Assert.IsTrue(_queue.Advance(After(1)));
            Assert.IsNotNull(_queue.ActiveCard);
        }

        [Test]
        public void Advance_ManyMatured_ShowsOneAndHoldsTheRest()
        {
            _queue.StartEmpty(T0);
            _queue.Advance(After(3));

            Assert.IsNotNull(_queue.ActiveCard);
            Assert.AreEqual(3, _queue.HeldCount);
            Assert.AreEqual(5, _queue.ReplenishingCount);
        }

        [Test]
        public void Advance_ReportsAMaturedSlotEvenWhenACardIsAlreadyShown()
        {
            _queue.StartEmpty(T0);
            _queue.Advance(After(1));
            var shown = _queue.ActiveCard;

            Assert.IsTrue(_queue.Advance(After(2)), "The held count changed, so the caller must hear of it.");
            Assert.AreSame(shown, _queue.ActiveCard);
            Assert.AreEqual(2, _queue.HeldCount);
        }

        [Test]
        public void Advance_NeverHoldsMoreThanTheCap()
        {
            _queue.StartEmpty(T0);
            _queue.Advance(T0.AddDays(3));

            Assert.AreEqual(8, _queue.HeldCount);
            Assert.AreEqual(0, _queue.ReplenishingCount);
        }

        [Test]
        public void HeldPlusReplenishing_AlwaysEqualsTheCap()
        {
            _queue.StartEmpty(T0);
            for (int i = 0; i <= 9; i++)
            {
                _queue.Advance(After(i));
                Assert.AreEqual(_queue.MaxCount, _queue.HeldCount + _queue.ReplenishingCount);
                if (i % 3 == 0) _queue.TakeActiveCard(After(i));
            }
        }

        // ── TakeActiveCard ────────────────────────────────────────────────────────

        [Test]
        public void TakeActiveCard_WithNothingShown_ReturnsNull()
        {
            _queue.StartEmpty(T0);
            Assert.IsNull(_queue.TakeActiveCard(T0));
        }

        [Test]
        public void TakeActiveCard_ReturnsTheShownCardAndStartsReplenishingItsSlot()
        {
            _queue.StartEmpty(T0);
            _queue.Advance(After(1));
            var shown = _queue.ActiveCard;

            Assert.AreSame(shown, _queue.TakeActiveCard(After(1)));
            Assert.IsNull(_queue.ActiveCard, "Nothing else was held, so nothing is shown.");
            Assert.AreEqual(8, _queue.ReplenishingCount);
        }

        [Test]
        public void TakeActiveCard_ShowsTheNextHeldCardImmediately()
        {
            _queue.StartEmpty(T0);
            _queue.Advance(After(8));

            _queue.TakeActiveCard(After(8));

            Assert.IsNotNull(_queue.ActiveCard);
            Assert.AreEqual(7, _queue.HeldCount);
            Assert.AreEqual(1, _queue.ReplenishingCount);
        }

        // ── CompleteReplenishment ─────────────────────────────────────────────────

        [Test]
        public void CompleteReplenishment_FillsTheHandAndShowsACard()
        {
            _queue.StartEmpty(T0);
            _queue.CompleteReplenishment();

            Assert.AreEqual(8, _queue.HeldCount);
            Assert.IsNotNull(_queue.ActiveCard);
        }

        // ── Timing ────────────────────────────────────────────────────────────────

        [Test]
        public void SecondsUntilNextAndFull_FollowTheTimer()
        {
            _queue.StartEmpty(T0);
            var now = T0.AddSeconds(20);

            Assert.AreEqual(100f, _queue.SecondsUntilNextCard(now), 0.01f);
            Assert.AreEqual(Interval * 8 - 20f, _queue.SecondsUntilFull(now), 0.01f);
        }

        // ── Restore ───────────────────────────────────────────────────────────────

        [Test]
        public void Restore_BringsBackTheShownCardDeckAndTimer()
        {
            _queue.Restore("b", new[] { "c" }, "b", replenishingCount: 3, nextReplenishDueUtc: After(1));

            Assert.AreEqual("b", _queue.ActiveCard.CardId);
            CollectionAssert.AreEqual(new[] { "c" }, _queue.RemainingDeckCardIds());
            Assert.AreEqual("b", _queue.LastDrawnCardId);
            Assert.AreEqual(3, _queue.ReplenishingCount);
            Assert.AreEqual(After(1), _queue.NextReplenishDueUtc);
        }

        /// <summary>
        /// With a full hand nothing is pending, so nothing would ever mature and draw a card:
        /// without the replacement the screen would sit on "8/8" with no card, forever.
        /// </summary>
        [Test]
        public void Restore_ACardDeletedSinceTheSave_IsReplacedByAHeldCard()
        {
            _queue.Restore("deleted", null, null, replenishingCount: 0, nextReplenishDueUtc: After(1));

            Assert.IsNotNull(_queue.ActiveCard);
            Assert.AreEqual(8, _queue.HeldCount);
        }

        [Test]
        public void Restore_WithNothingHeld_ShowsNoCard()
        {
            _queue.Restore(null, null, null, replenishingCount: 8, nextReplenishDueUtc: After(1));
            Assert.IsNull(_queue.ActiveCard);
        }
    }
}
