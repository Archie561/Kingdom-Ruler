using System;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Trade.Domain
{
    /// <summary>Why a trade offer cannot be taken — or why taking it will cost the player something.</summary>
    public enum TradeIssueKind
    {
        /// <summary>The player does not hold enough of a resource the offer asks for. <b>Blocks.</b></summary>
        InsufficientResource,

        /// <summary>The offer was refreshed away or already taken. <b>Blocks.</b></summary>
        OfferUnavailable,

        /// <summary>
        /// A received resource has more coming than the warehouse can hold. <b>Warns only.</b>
        /// </summary>
        WouldOverflowWarehouse
    }

    /// <summary>
    /// One reason an offer is problematic, naming the resource at fault and the numbers behind
    /// it, so the confirmation panel can say exactly what is wrong (<c>GDD.md</c> §7 — "a clear
    /// inline message explaining which resource is the blocker").
    /// </summary>
    /// <remarks>
    /// <para><b>Overflow warns, it does not block</b> — see <see cref="BlocksAcceptance"/>. This
    /// is the design rule that shapes the whole accept path, and it is easy to undo by accident
    /// because "the warehouse is full" reads like a reason to refuse. It is not: the player is
    /// shown what will be lost and may proceed anyway. Only being unable to pay the give side
    /// actually prevents a trade.</para>
    ///
    /// <para>A pure Domain type: it carries a kind, a resource and two numbers, and nothing else.
    /// Turning those into a sentence is the Presenter's job, because that is where
    /// <c>ILocalizationService</c> lives.</para>
    /// </remarks>
    public readonly struct TradeIssue
    {
        public TradeIssueKind    Kind     { get; }
        public TradeResourceType Resource { get; }

        /// <summary>How much the offer asks for, or how much it would deliver.</summary>
        public float Required { get; }

        /// <summary>How much the player holds, or how much free space the warehouse has.</summary>
        public float Available { get; }

        public TradeIssue(TradeIssueKind kind, TradeResourceType resource,
                          float required, float available)
        {
            Kind      = kind;
            Resource  = resource;
            Required  = required;
            Available = available;
        }

        /// <summary>The gap between what is needed and what there is. Never negative.</summary>
        public float Shortfall => Math.Max(0f, Required - Available);

        /// <summary>
        /// Whether this issue prevents the trade. True for everything except
        /// <see cref="TradeIssueKind.WouldOverflowWarehouse"/>, which the player may accept and
        /// forfeit the excess.
        /// </summary>
        public bool BlocksAcceptance => Kind != TradeIssueKind.WouldOverflowWarehouse;

        /// <summary>An offer that no longer exists — the only issue with no meaningful resource.</summary>
        public static TradeIssue Unavailable() =>
            new TradeIssue(TradeIssueKind.OfferUnavailable, default, 0f, 0f);

        public override string ToString() =>
            $"{Kind}({Resource}: need {Required}, have {Available})";
    }
}
