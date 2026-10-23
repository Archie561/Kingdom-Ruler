using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using KingdomRuler.Modules.Laws;

namespace KingdomRuler.Tests.EditMode.Modules.Laws
{
    [TestFixture]
    public sealed class LawDeckTests
    {
        private readonly List<ScriptableObject> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in _created)
                if (asset != null) Object.DestroyImmediate(asset);
            _created.Clear();
        }

        private LawCardDefinition Card(string id)
        {
            var card = ScriptableObject.CreateInstance<LawCardDefinition>();
            card.CardId = id;
            _created.Add(card);
            return card;
        }

        private LawCardDefinition[] Cards(int count)
        {
            var cards = new LawCardDefinition[count];
            for (int i = 0; i < count; i++) cards[i] = Card($"card_{i}");
            return cards;
        }

        private static LawDeck Deck(int seed = 12345) => new LawDeck(new System.Random(seed));

        // ── The shuffle-bag guarantee ─────────────────────────────────────────────

        [Test]
        public void Draw_WithinOneCycle_YieldsEveryCardExactlyOnce()
        {
            var deck = Deck();
            deck.SetContents(Cards(6));

            var seen = new List<string>();
            for (int i = 0; i < 6; i++) seen.Add(deck.Draw().CardId);

            CollectionAssert.AllItemsAreUnique(seen);
            Assert.AreEqual(6, seen.Count);
        }

        [Test]
        public void Draw_PastTheEndOfACycle_StartsANewOne()
        {
            var deck = Deck();
            deck.SetContents(Cards(3));

            for (int i = 0; i < 3; i++) deck.Draw();
            Assert.AreEqual(0, deck.RemainingCardIds().Count);

            Assert.IsNotNull(deck.Draw());
        }

        [Test]
        public void Draw_AcrossManyCycleBoundaries_NeverRepeatsBackToBack()
        {
            for (int seed = 0; seed < 50; seed++)
            {
                var deck = Deck(seed);
                deck.SetContents(Cards(4));

                string previous = null;
                for (int i = 0; i < 40; i++)
                {
                    string current = deck.Draw().CardId;
                    Assert.AreNotEqual(previous, current, $"Back-to-back repeat of '{current}' with seed {seed}.");
                    previous = current;
                }
            }
        }

        [Test]
        public void Draw_WithASingleCard_RepeatsWithoutThrowing()
        {
            var deck = Deck();
            deck.SetContents(Cards(1));
            Assert.DoesNotThrow(() => { for (int i = 0; i < 5; i++) deck.Draw(); });
        }

        [Test]
        public void Draw_WithNoContent_ReturnsNull()
        {
            var deck = Deck();
            deck.SetContents(new LawCardDefinition[0]);
            Assert.IsNull(deck.Draw());
        }

        [Test]
        public void SetContents_SkipsNullEntries()
        {
            var deck = Deck();
            deck.SetContents(new[] { Card("a"), null, Card("b") });

            Assert.AreEqual(2, deck.RemainingCardIds().Count);
        }

        [Test]
        public void SetContents_IsShuffledNotSourceOrdered()
        {
            bool anyDiffered = false;
            for (int seed = 0; seed < 20 && !anyDiffered; seed++)
            {
                var deck = Deck(seed);
                deck.SetContents(Cards(8));

                for (int i = 0; i < 8; i++)
                    if (deck.Draw().CardId != $"card_{i}") { anyDiffered = true; break; }
            }
            Assert.IsTrue(anyDiffered, "Draw order never differed from authored order.");
        }

        // ── Save round trip ───────────────────────────────────────────────────────

        [Test]
        public void RestoreCycle_ResumesMidCycleRatherThanReshuffling()
        {
            var cards = Cards(5);
            var deck = Deck();
            deck.SetContents(cards);
            deck.Draw();
            deck.Draw();

            var restored = Deck();
            restored.SetContents(cards);
            restored.RestoreCycle(deck.RemainingCardIds(), deck.LastDrawnCardId);

            Assert.AreEqual(deck.LastDrawnCardId, restored.LastDrawnCardId);
            CollectionAssert.AreEqual(deck.RemainingCardIds(), restored.RemainingCardIds());
        }

        [Test]
        public void RestoreCycle_SkipsIdsThatNoLongerExist()
        {
            var deck = Deck();
            deck.SetContents(Cards(3));

            deck.RestoreCycle(new[] { "card_0", "deleted_card", "card_2" }, "card_1");

            CollectionAssert.AreEqual(new[] { "card_0", "card_2" }, deck.RemainingCardIds());
        }

        [Test]
        public void RestoreCycle_WhenNothingSurvives_StartsAFreshCycle()
        {
            var deck = Deck();
            deck.SetContents(Cards(4));

            deck.RestoreCycle(new[] { "gone_a", "gone_b" }, null);

            Assert.AreEqual(4, deck.RemainingCardIds().Count);
        }

        [Test]
        public void RestoreCycle_WithNullIds_StartsAFreshCycle()
        {
            var deck = Deck();
            deck.SetContents(Cards(4));

            deck.RestoreCycle(null, null);

            Assert.AreEqual(4, deck.RemainingCardIds().Count);
        }

        [Test]
        public void FindById_ReturnsTheCardOrNull()
        {
            var deck = Deck();
            deck.SetContents(Cards(3));

            Assert.IsNotNull(deck.FindById("card_1"));
            Assert.IsNull(deck.FindById("nope"));
            Assert.IsNull(deck.FindById(null));
            Assert.IsNull(deck.FindById(""));
        }
    }
}
