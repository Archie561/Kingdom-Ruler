using System;
using KingdomRuler.Shared.Ledger;

namespace KingdomRuler.Modules.Cities.Domain
{
    [Serializable]
    public struct ResourceCost
    {
        public TradeResourceType Type;
        public float Amount;

        public ResourceCost(TradeResourceType type, float amount)
        {
            Type = type;
            Amount = amount;
        }
    }
}
