using System;

namespace KingdomRuler.Modules.Trade.UI
{
    /// <summary>What the offer confirmation draws, already localized.</summary>
    /// <remarks>
    /// <see cref="IEquatable{T}"/>, comparing the arrays element by element: the popup re-reads its
    /// data several times a second and redraws only on a change, and the arrays are rebuilt each read.
    /// </remarks>
    public readonly struct TradeOfferPopupData : IEquatable<TradeOfferPopupData>
    {
        public readonly ResourceAmountViewData[] Give;
        public readonly ResourceAmountViewData[] Receive;

        /// <summary>One line per resource the player cannot pay, or null. Any blocker disables Accept.</summary>
        public readonly string Blockers;

        /// <summary>One line per resource that will lose units to a full warehouse, or null. Accept stays enabled.</summary>
        public readonly string Warnings;

        public readonly bool CanAccept;

        public TradeOfferPopupData(ResourceAmountViewData[] give, ResourceAmountViewData[] receive,
                                   string blockers, string warnings, bool canAccept)
        {
            Give      = give;
            Receive   = receive;
            Blockers  = blockers;
            Warnings  = warnings;
            CanAccept = canAccept;
        }

        public bool Equals(TradeOfferPopupData other) =>
            CanAccept == other.CanAccept && Blockers == other.Blockers && Warnings == other.Warnings
            && ResourceAmountViewData.SequenceEqual(Give, other.Give)
            && ResourceAmountViewData.SequenceEqual(Receive, other.Receive);

        public override bool Equals(object obj) => obj is TradeOfferPopupData other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(CanAccept, Blockers, Warnings);
    }
}
