using System;
using System.Collections.Generic;

namespace KingdomRuler.Modules.Trade
{
    /// <summary>What happened when the player accepted an offer, including what did not fit.</summary>
    /// <remarks>
    /// A loss here is expected, not a bug: overflow warns rather than blocks, so the player may
    /// knowingly accept an offer that overspills, and <see cref="Forfeited"/> is what they chose to
    /// give up. Per line, <c>received + forfeited == offered</c>; never assert received == offered.
    /// </remarks>
    public readonly struct TradeAcceptResult
    {
        private readonly IReadOnlyList<TradeIssue>  _issues;
        private readonly IReadOnlyList<TradeAmount> _received;
        private readonly IReadOnlyList<TradeAmount> _forfeited;

        private TradeAcceptResult(bool accepted, IReadOnlyList<TradeIssue> issues,
                                  IReadOnlyList<TradeAmount> received, IReadOnlyList<TradeAmount> forfeited)
        {
            Accepted   = accepted;
            _issues    = issues;
            _received  = received;
            _forfeited = forfeited;
        }

        public bool Accepted { get; }

        /// <summary>Why it was refused; or, when it went through, the overflow warnings that applied. Never null.</summary>
        public IReadOnlyList<TradeIssue> Issues => _issues ?? Array.Empty<TradeIssue>();

        /// <summary>What actually went into the warehouses, one line per received resource. Empty on a refusal.</summary>
        public IReadOnlyList<TradeAmount> Received => _received ?? Array.Empty<TradeAmount>();

        /// <summary>What did not fit, only for resources that lost something. Empty when everything fit.</summary>
        public IReadOnlyList<TradeAmount> Forfeited => _forfeited ?? Array.Empty<TradeAmount>();

        public static TradeAcceptResult Success(IReadOnlyList<TradeAmount> received, IReadOnlyList<TradeAmount> forfeited,
                                                IReadOnlyList<TradeIssue> warnings) =>
            new TradeAcceptResult(true, warnings, received, forfeited);

        /// <summary>The trade was refused and nothing was spent.</summary>
        public static TradeAcceptResult Refused(IReadOnlyList<TradeIssue> issues) =>
            new TradeAcceptResult(false, issues, null, null);
    }
}
