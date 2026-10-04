using System.Collections.Generic;
using System.Linq;

namespace KingdomRuler.Modules.Trade.Domain
{
    /// <summary>
    /// Everything the confirmation panel needs to know about an offer, computed <b>without
    /// mutating anything</b>.
    /// </summary>
    /// <remarks>
    /// <para>Separating "can this be taken, and what should I warn about" from actually taking it
    /// is what lets the panel be opened, read and dismissed with no side effects — and it is what
    /// makes the accept path testable without a UI.</para>
    ///
    /// <para><see cref="CanAccept"/> counts only <em>blocking</em> issues. An offer whose only
    /// problem is a full warehouse is still acceptable (<c>GDD.md</c> §7 as amended): the player
    /// sees the warning and decides. Do not "fix" this to include every issue.</para>
    /// </remarks>
    public readonly struct TradeOfferEvaluation
    {
        private readonly IReadOnlyList<TradeIssue> _issues;

        /// <summary>False when the id matched no live offer.</summary>
        public bool OfferExists { get; }

        /// <summary>Every issue found, blocking and advisory alike. Never null.</summary>
        public IReadOnlyList<TradeIssue> Issues => _issues ?? System.Array.Empty<TradeIssue>();

        public TradeOfferEvaluation(bool offerExists, IReadOnlyList<TradeIssue> issues)
        {
            OfferExists = offerExists;
            _issues     = issues;
        }

        /// <summary>
        /// Whether the trade may proceed. True when nothing <em>blocking</em> is wrong — an
        /// overflow warning on its own leaves this true.
        /// </summary>
        public bool CanAccept => OfferExists && !Issues.Any(issue => issue.BlocksAcceptance);

        /// <summary>Whether any resource would overspill its warehouse if this is accepted.</summary>
        public bool HasOverflowWarning =>
            Issues.Any(issue => issue.Kind == TradeIssueKind.WouldOverflowWarehouse);

        /// <summary>Total units that would be lost to full warehouses. Zero when nothing overflows.</summary>
        public float TotalForfeited =>
            Issues.Where(issue => issue.Kind == TradeIssueKind.WouldOverflowWarehouse)
                  .Sum(issue => issue.Shortfall);

        /// <summary>The result for an id that matched nothing.</summary>
        public static TradeOfferEvaluation Missing() =>
            new TradeOfferEvaluation(false, new[] { TradeIssue.Unavailable() });
    }
}
