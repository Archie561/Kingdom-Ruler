using System;
using System.Collections.Generic;

namespace KingdomRuler.Modules.Trade
{
    /// <summary>Everything wrong with one offer right now, found without changing anything.</summary>
    /// <remarks>
    /// Separate from accepting so the confirmation can be opened, read and dismissed with no side
    /// effects. <see cref="CanAccept"/> counts only <i>blocking</i> issues: an offer whose only
    /// problem is a full warehouse can still be accepted.
    /// </remarks>
    public readonly struct TradeOfferEvaluation
    {
        private readonly IReadOnlyList<TradeIssue> _issues;

        public TradeOfferEvaluation(bool offerExists, IReadOnlyList<TradeIssue> issues)
        {
            OfferExists = offerExists;
            _issues     = issues;
        }

        /// <summary>False when the id matched no live offer.</summary>
        public bool OfferExists { get; }

        /// <summary>Every issue, blocking and warning alike, in the offer's line order. Never null.</summary>
        public IReadOnlyList<TradeIssue> Issues => _issues ?? Array.Empty<TradeIssue>();

        /// <summary>The offer exists and nothing <i>blocking</i> is wrong; an overflow on its own leaves this true.</summary>
        public bool CanAccept
        {
            get
            {
                if (!OfferExists) return false;
                foreach (var issue in Issues)
                    if (issue.BlocksAcceptance) return false;
                return true;
            }
        }

        /// <summary>The evaluation of an id that matched nothing.</summary>
        public static TradeOfferEvaluation Missing() => new TradeOfferEvaluation(false, new[] { TradeIssue.Unavailable() });
    }
}
