namespace KingdomRuler.Modules.Trade.UI
{
    /// <summary>
    /// One offer in the list: what is given and what is received. Nothing about whether the player
    /// can afford it or what will not fit — that is shown in the confirmation only (GDD §7).
    /// </summary>
    public readonly struct OfferRowViewData
    {
        public readonly string OfferId;
        public readonly ResourceAmountViewData[] Give;
        public readonly ResourceAmountViewData[] Receive;

        public OfferRowViewData(string offerId, ResourceAmountViewData[] give, ResourceAmountViewData[] receive)
        {
            OfferId = offerId;
            Give    = give;
            Receive = receive;
        }
    }
}
