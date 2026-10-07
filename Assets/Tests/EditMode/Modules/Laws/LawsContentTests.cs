using System.Collections.Generic;
using NUnit.Framework;
using KingdomRuler.Modules.Laws;
using KingdomRuler.Tests.EditMode.Systems;

namespace KingdomRuler.Tests.EditMode.Modules.Laws
{
    /// <summary>
    /// Checks the <b>real</b> law cards and Laws text in the project, rather than data a test
    /// builds for itself.
    /// </summary>
    /// <remarks>
    /// A wiring mistake fails; an untranslated entry only warns (<c>ARCHITECTURE.md</c> §8). The
    /// screen's fixed labels (ACCEPT, REJECT, the waiting line) are not checked: they are on
    /// screen the moment the Laws tab opens, so a gap there cannot go unnoticed.
    /// </remarks>
    public sealed class LawsContentTests
    {
        /// <summary>
        /// A card's text keys are derived from its <c>CardId</c>, so an empty id has no text and
        /// two cards sharing an id would silently show the same text.
        /// </summary>
        [Test]
        public void EveryCard_HasAUniqueId()
        {
            var problems = new List<string>();
            var cardWithId = new Dictionary<string, LawCardDefinition>();

            foreach (var config in ProjectAssets.All<LawsConfig>())
            {
                if (config.AllCards == null) continue;

                foreach (var card in config.AllCards)
                {
                    if (card == null)
                        problems.Add($"{config.name}: AllCards has an empty slot.");
                    else if (string.IsNullOrWhiteSpace(card.CardId))
                        problems.Add($"{card.name}: CardId is empty, so its text cannot be keyed.");
                    else if (!cardWithId.TryGetValue(card.CardId, out var first))
                        cardWithId.Add(card.CardId, card);
                    else if (first != card)   // the same asset in two configs is fine
                        problems.Add($"{card.name} and {first.name} share the CardId '{card.CardId}'.");
                }
            }

            CollectionAssert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void EveryCard_HasItsTextInEveryLocale()
        {
            // card.TitleKey / card.FlavorKey — the same properties the Presenter resolves.
            var text = new LocalizationCheck(LawCardDefinition.StringTable);
            foreach (var config in ProjectAssets.All<LawsConfig>())
            {
                if (config.AllCards == null) continue;

                foreach (var card in config.AllCards)
                {
                    if (card == null || string.IsNullOrWhiteSpace(card.CardId)) continue;   // EveryCard_HasAUniqueId
                    text.Require(card.TitleKey);
                    text.Require(card.FlavorKey);
                }
            }
            text.Report();
        }

        /// <summary>The refill popup only appears when the queue is short — easy never to see.</summary>
        [Test]
        public void TheRefillMessages_ExistInEveryLocale()
        {
            var text = new LocalizationCheck(LawsUIText.StringTable);
            text.Require(LawsUIText.RefillTitle);
            text.Require(LawsUIText.RefillBody);
            text.Report();
        }
    }
}
