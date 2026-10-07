using System;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Cities.Domain
{
    public static class CityAffordabilityChecker
    {
        /// <summary>
        /// Checks if the player can afford a city given current ledger state.
        /// Returns (canAfford, failReason).
        /// </summary>
        public static (bool CanAfford, string FailReason) Check(
            KingdomLedger ledger,
            CharacteristicRequirement[] charRequirements,
            ResourceCost[] resourceCosts,
            long goldCost)
        {
            // Check characteristic levels (not spent, just gated)
            if (charRequirements != null)
            {
                foreach (var req in charRequirements)
                {
                    var state = ledger.GetCharacteristic(req.Type);
                    if (state.Level < req.RequiredLevel)
                        return (false, $"{req.Type} level {state.Level} < required {req.RequiredLevel}");
                }
            }

            // Check gold
            if (goldCost > 0 && ledger.Gold < goldCost)
                return (false, $"Gold {ledger.Gold} < required {goldCost}");

            // Check trade resources
            if (resourceCosts != null)
            {
                foreach (var cost in resourceCosts)
                {
                    if (!ledger.HasTradeResource(cost.Type, cost.Amount))
                        return (false, $"{cost.Type} insufficient");
                }
            }

            return (true, null);
        }
    }
}
