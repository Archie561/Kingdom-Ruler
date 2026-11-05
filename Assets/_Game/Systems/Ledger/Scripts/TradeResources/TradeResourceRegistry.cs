using UnityEngine;

namespace KingdomRuler.Systems.Ledger
{
    /// <summary>Shared display data for the six trade resources.</summary>
    [CreateAssetMenu(
        fileName = "TradeResourceRegistry",
        menuName = "Kingdom Ruler/Systems/Trade Resource Registry")]
    public sealed class TradeResourceRegistry : DefinitionRegistry<TradeResourceType, TradeResourceDefinition>
    {
        protected override string BuildNameKey(TradeResourceType type) => TradeResourceDefinition.BuildNameKey(type);
    }
}
