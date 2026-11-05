using UnityEngine;

namespace KingdomRuler.Systems.Ledger
{
    /// <summary>Shared display data for the six characteristics.</summary>
    [CreateAssetMenu(
        fileName = "CharacteristicRegistry",
        menuName = "Kingdom Ruler/Systems/Characteristic Registry")]
    public sealed class CharacteristicRegistry : DefinitionRegistry<CharacteristicType, CharacteristicDefinition>
    {
        protected override string BuildNameKey(CharacteristicType type) => CharacteristicDefinition.BuildNameKey(type);
    }
}
