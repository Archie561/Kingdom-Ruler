using System;
using System.Collections.Generic;
using KingdomRuler.Shared.Ledger;

namespace KingdomRuler.Modules.Trade.Domain
{
    /// <summary>
    /// How good a deal an offer is. All six resources are valued identically (<c>GDD.md</c> §7),
    /// so this is decided purely by the ratio of units received to units given.
    /// </summary>
    public enum TradeProfitability
    {
        Profitable,
        Neutral,
        Unprofitable
    }

    /// <summary>
    /// A single trade offer. Pure data, no Unity dependencies.
    /// </summary>
    /// <remarks>
    /// <para><b>Immutable.</b> The dictionaries are copied on construction and exposed as
    /// <see cref="IReadOnlyDictionary{TKey,TValue}"/>, so a Presenter or View holding a live offer
    /// cannot edit the terms of a trade the Manager is about to execute.</para>
    ///
    /// <para>Totals are computed once here rather than summed on demand: the offer row renders
    /// them and the row is re-read while the list scrolls.</para>
    /// </remarks>
    public sealed class TradeOffer
    {
        /// <summary>
        /// Stable identity, used to accept the offer. Not its index: the 20-minute auto-refresh
        /// can land between the player tapping a row and confirming, and an index would then
        /// execute whichever offer had taken that slot.
        /// </summary>
        public string Id { get; }

        public IReadOnlyDictionary<TradeResourceType, float> GiveResources { get; }
        public IReadOnlyDictionary<TradeResourceType, float> ReceiveResources { get; }
        public TradeProfitability Profitability { get; }

        /// <summary>Total units the player must give.</summary>
        public float TotalGive { get; }

        /// <summary>Total units the player receives, before any warehouse cap is applied.</summary>
        public float TotalReceive { get; }

        public TradeOffer(
            string id,
            IReadOnlyDictionary<TradeResourceType, float> give,
            IReadOnlyDictionary<TradeResourceType, float> receive,
            TradeProfitability profitability)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            if (give == null) throw new ArgumentNullException(nameof(give));
            if (receive == null) throw new ArgumentNullException(nameof(receive));

            GiveResources    = new Dictionary<TradeResourceType, float>(ToDictionary(give));
            ReceiveResources = new Dictionary<TradeResourceType, float>(ToDictionary(receive));
            Profitability    = profitability;

            TotalGive    = Sum(GiveResources);
            TotalReceive = Sum(ReceiveResources);
        }

        private static Dictionary<TradeResourceType, float> ToDictionary(
            IReadOnlyDictionary<TradeResourceType, float> source)
        {
            var copy = new Dictionary<TradeResourceType, float>(source.Count);
            foreach (var pair in source) copy[pair.Key] = pair.Value;
            return copy;
        }

        private static float Sum(IReadOnlyDictionary<TradeResourceType, float> amounts)
        {
            float total = 0f;
            foreach (var pair in amounts) total += pair.Value;
            return total;
        }

        public override string ToString() =>
            $"TradeOffer({Id}, {Profitability}, give {TotalGive} → receive {TotalReceive})";
    }
}
