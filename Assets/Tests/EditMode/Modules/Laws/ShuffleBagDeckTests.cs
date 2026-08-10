using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using KingdomRuler.Modules.Laws;
using KingdomRuler.Modules.Laws.Domain;

namespace KingdomRuler.Tests.EditMode.Modules.Laws
{
    /// <summary>
    /// Draw order in isolation, with a seeded RNG so every case is reproducible.
    /// </summary>
    [TestFixture]
    public sealed class ShuffleBagDeckTests
    {
        private readonly List<ScriptableObject> _created = new();

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

        private LawCardDefinition[] Cards(int count)
        {
            var cards = new LawCardDefinition[count];
            for (int i = 0; i < count; i++) cards[i] = Card($"card_{i}");
            return cards;
        }

        private static ShuffleBagDeck Deck(int seed = 12345) => new ShuffleBagDeck(new System.Random(seed));

        // ── The shuffle-bag guarantee ─────────────────────────────────────────────

        [Test]
        public void Draw_WithinOneCycle_YieldsEveryCardExactlyOnce()
        {
            var deck = Deck();
            deck.SetContents(Cards(6));

            var seen = new List<string>();
            for (int i = 0; i < 6; i++) seen.Add(deck.Draw().CardId);

            CollectionAssert.AllItemsAreUnique(seen, "A cycle must not repeat a card.");
            Assert.AreEqual(6, seen.Count);
        }

        [Test]
        public void Draw_PastTheEndOfACycle_StartsANewOne()
        {
            var deck = Deck();
            deck.SetContents(Cards(3));

            for (int i = 0; i < 3; i++) deck.Draw();
            Assert.AreEqual(0, deck.RemainingCount);

            Assert.IsNotNull(deck.Draw(), "The bag must refill rather than run dry.");
        }

        /// <summary>
        /// The cycle boundary is the one place the bag would otherwise allow a repeat,
        /// and it is exactly where the player would notice one.
        /// </summary>
        [Test]
        public void Draw_AcrossManyCycleBoundaries_NeverRepeatsBackToBack()
        {
            for (int seed = 0; seed < 50; seed++)
            {
                var deck = Deck(seed);
                deck.SetContents(Cards(4));

                string previous = null;
                for (int i = 0; i < 40; i++)                    // 10 full cycles
                {
                    string current = deck.Draw().CardId;
                    Assert.AreNotEqual(previous, current,
                        $"Back-to-back repeat of '{current}' with seed {seed}.");
                    previous = current;
                }
            }
        }

        [Test]
        public void Draw_WithASingleCard_RepeatsWithoutThrowing()
        {
            var deck = Deck();
            deck.SetContents(Cards(1));

            // With one card a repeat is unavoidable; it must simply not break.
            Assert.DoesNotThrow(() => { for (int i = 0; i < 5; i++) deck.Draw(); });
        }

        [Test]
        public void Draw_WithNoContent_ReturnsNull()
        {
            var deck = Deck();
            deck.SetContents(Array.Empty<LawCardDefinition>());
            Assert.IsNull(deck.Draw());
            Assert.IsTrue(deck.IsEmpty);
        }

        [Test]
        public void SetContents_DropsNullEntries()
        {
            var deck = Deck();
            deck.SetContents(new[] { Card("a"), null, Card("b") });

            Assert.AreEqual(2, deck.RemainingCount);
            Assert.IsNotNull(deck.Draw());
            Assert.IsNotNull(deck.Draw());
        }

        [Test]
        public void SetContents_IsShuffledNotSourceOrdered()
        {
            // Across many seeds at least one must differ from the authored order,
            // otherwise the bag is not shuffling at all.
            bool anyDiffered = false;
            for (int seed = 0; seed < 20 && !anyDiffered; seed++)
            {
                var deck = Deck(seed);
                deck.SetContents(Cards(8));

                var drawn = new List<string>();
                for (int i = 0; i < 8; i++) drawn.Add(deck.Draw().CardId);

                for (int i = 0; i < 8; i++)
                    if (drawn[i] != $"card_{i}") { anyDiffered = true; break; }
            }
            Assert.IsTrue(anyDiffered, "Draw order never differed from authored order.");
        }

        // ── Save round trip ───────────────────────────────────────────────────────

        [Test]
        public void RestoreCycle_ResumesMidCycleRatherThanReshuffling()
        {
            var deck = Deck();
            deck.SetContents(Cards(5));
            deck.Draw();
            deck.Draw();

            var remaining = deck.RemainingCardIds();
            var lastDrawn = deck.LastDrawnCardId;

            var restored = Deck();
            restored.SetContents(Cards(5));
            restored.RestoreCycle(remaining, lastDrawn);

            Assert.AreEqual(3, restored.RemainingCount);
            Assert.AreEqual(lastDrawn, restored.LastDrawnCardId);
            CollectionAssert.AreEqual(remaining, restored.RemainingCardIds(),
                "The saved cycle order must be preserved exactly.");
        }

        [Test]
        public void RestoreCycle_DropsIdsThatNoLongerExist()
        {
            var deck = Deck();
            deck.SetContents(Cards(3));

            deck.RestoreCycle(new[] { "card_0", "deleted_card", "card_2" }, "card_1");

            Assert.AreEqual(2, deck.RemainingCount, "A card removed since the save must be skipped.");
        }

        [Test]
        public void RestoreCycle_WhenNothingSurvives_StartsAFreshCycle()
        {
            var deck = Deck();
            deck.SetContents(Cards(4));

            deck.RestoreCycle(new[] { "gone_a", "gone_b" }, null);

            Assert.AreEqual(4, deck.RemainingCount, "A dead deck must recover, not stay empty.");
        }

        [Test]
        public void RestoreCycle_WithNullIds_StartsAFreshCycle()
        {
            var deck = Deck();
            deck.SetContents(Cards(4));

            deck.RestoreCycle(null, null);

            Assert.AreEqual(4, deck.RemainingCount);
        }

        // ── Lookup ────────────────────────────────────────────────────────────────

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
