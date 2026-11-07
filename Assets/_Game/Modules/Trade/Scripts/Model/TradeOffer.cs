using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Trade
{
    /// <summary>One trade offer: what the player gives, what they receive, and how good a deal it is.</summary>
    /// <remarks>
    /// Immutable, and valid by construction: neither side is empty, every amount is at least 1, and
    /// no resource appears twice — on one side or across both. (The 2–3 resources a side is the
    /// generator's rule, not the offer's, so a saved offer that lost an unknown resource still
    /// loads.) A Presenter holding an offer can
    /// therefore never change the terms of a trade the Manager is about to execute, and nothing
    /// downstream has to re-check them.
    /// </remarks>
    public sealed class TradeOffer
    {
        /// <summary>
        /// Stable identity, used to accept the offer. Never its index: a refresh can land between
        /// the tap and the confirmation, and an index would then execute whichever offer took the slot.
        /// </summary>
        public string Id { get; }

        /// <summary>What the player gives, in display order.</summary>
        public IReadOnlyList<TradeAmount> Give { get; }

        /// <summary>What the player receives before any warehouse limit, in display order.</summary>
        public IReadOnlyList<TradeAmount> Receive { get; }

        public TradeProfitability Profitability { get; }

        public int TotalGive    { get; }
        public int TotalReceive { get; }

        /// <exception cref="ArgumentException">The terms break one of the rules in the remarks.</exception>
        public TradeOffer(string id, IReadOnlyList<TradeAmount> give, IReadOnlyList<TradeAmount> receive,
                          TradeProfitability profitability)
        {
            string problem = FindProblem(id, give, receive);
            if (problem != null) throw new ArgumentException(problem);

            Id            = id;
            Give          = Copy(give);
            Receive       = Copy(receive);
            Profitability = profitability;
            TotalGive     = Sum(Give);
            TotalReceive  = Sum(Receive);
        }

        /// <summary>The constructor for terms that may be damaged, such as a save: false instead of a throw.</summary>
        public static bool TryCreate(string id, IReadOnlyList<TradeAmount> give, IReadOnlyList<TradeAmount> receive,
                                     TradeProfitability profitability, out TradeOffer offer)
        {
            offer = FindProblem(id, give, receive) == null
                ? new TradeOffer(id, give, receive, profitability)
                : null;
            return offer != null;
        }

        public override string ToString() => $"TradeOffer({Id}, {Profitability}, give {TotalGive} → receive {TotalReceive})";

        private static string FindProblem(string id, IReadOnlyList<TradeAmount> give, IReadOnlyList<TradeAmount> receive)
        {
            if (string.IsNullOrEmpty(id)) return "An offer needs an id.";
            if (give == null || give.Count == 0) return "An offer must give something.";
            if (receive == null || receive.Count == 0) return "An offer must receive something.";

            var seen = new HashSet<TradeResourceType>();
            foreach (var side in new[] { give, receive })
                foreach (var amount in side)
                {
                    if (amount.Units < 1) return $"Offer {id}: {amount} is not a positive amount.";
                    if (!seen.Add(amount.Resource)) return $"Offer {id}: {amount.Resource} appears twice.";
                }
            return null;
        }

        private static IReadOnlyList<TradeAmount> Copy(IReadOnlyList<TradeAmount> source)
        {
            var copy = new TradeAmount[source.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = source[i];
            return new ReadOnlyCollection<TradeAmount>(copy);
        }

        private static int Sum(IReadOnlyList<TradeAmount> amounts)
        {
            int total = 0;
            foreach (var amount in amounts) total += amount.Units;
            return total;
        }
    }
}
