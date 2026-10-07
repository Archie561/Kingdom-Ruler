using System.Collections.Generic;
using System.Linq;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Trade.Domain
{
    /// <summary>
    /// What actually happened when an offer was accepted — including how much of it the player
    /// did not get room for.
    /// </summary>
    /// <remarks>
    /// <para><see cref="Received"/> holds the values <c>KingdomLedger.AddTradeResource</c>
    /// returned, not the amounts the offer advertised. That return value is the whole point: it
    /// says how much actually fit. The old code discarded it, which is why a full warehouse
    /// silently destroyed resources with nothing on screen to explain it.</para>
    ///
    /// <para><b>A shortfall here is expected, not a bug.</b> Because overflow warns rather than
    /// blocks, a player may knowingly accept an offer that overspills, and
    /// <see cref="Forfeited"/> is what they chose to give up. Do not add an assertion that
    /// received equals requested — the correct invariant is
    /// <c>received == min(requested, freeSpace)</c>.</para>
    /// </remarks>
    public readonly struct TradeAcceptResult
    {
        private readonly IReadOnlyList<TradeIssue> _issues;
        private readonly IReadOnlyDictionary<TradeResourceType, float> _received;
        private readonly IReadOnlyDictionary<TradeResourceType, float> _forfeited;

        /// <summary>Whether the trade went through.</summary>
        public bool Accepted { get; }

        /// <summary>
        /// Why it was refused, or — when it succeeded — the overflow warnings that applied.
        /// Never null.
        /// </summary>
        public IReadOnlyList<TradeIssue> Issues => _issues ?? System.Array.Empty<TradeIssue>();

        /// <summary>What the player actually gained, per resource. Empty on a refusal.</summary>
        public IReadOnlyDictionary<TradeResourceType, float> Received =>
            _received ?? EmptyAmounts;

        /// <summary>
        /// What was lost to full warehouses, per resource — the difference between the offer and
        /// <see cref="Received"/>. Empty when everything fit.
        /// </summary>
        public IReadOnlyDictionary<TradeResourceType, float> Forfeited =>
            _forfeited ?? EmptyAmounts;

        private static readonly IReadOnlyDictionary<TradeResourceType, float> EmptyAmounts =
            new Dictionary<TradeResourceType, float>();

        private TradeAcceptResult(bool accepted,
                                  IReadOnlyList<TradeIssue> issues,
                                  IReadOnlyDictionary<TradeResourceType, float> received,
                                  IReadOnlyDictionary<TradeResourceType, float> forfeited)
        {
            Accepted   = accepted;
            _issues    = issues;
            _received  = received;
            _forfeited = forfeited;
        }

        /// <summary>Whether anything was lost to a full warehouse.</summary>
        public bool HasForfeited => Forfeited.Count > 0;

        /// <summary>Total units lost to full warehouses.</summary>
        public float TotalForfeited => Forfeited.Values.Sum();

        /// <summary>The trade went through. <paramref name="issues"/> may hold overflow warnings.</summary>
        public static TradeAcceptResult Success(
            IReadOnlyDictionary<TradeResourceType, float> received,
            IReadOnlyDictionary<TradeResourceType, float> forfeited,
            IReadOnlyList<TradeIssue> issues) =>
            new TradeAcceptResult(true, issues, received, forfeited);

        /// <summary>The trade was refused and the ledger was not touched.</summary>
        public static TradeAcceptResult Refused(IReadOnlyList<TradeIssue> issues) =>
            new TradeAcceptResult(false, issues, null, null);
    }
}
