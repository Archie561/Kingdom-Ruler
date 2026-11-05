using System;
using UnityEngine;

namespace KingdomRuler.Systems.Ledger
{
    /// <summary>
    /// Shared display data for one member of <typeparamref name="TType"/> — what a
    /// <see cref="DefinitionRegistry{TType, TDefinition}"/> indexes.
    /// </summary>
    public interface IDefinition<out TType> where TType : struct, Enum
    {
        TType Type { get; }

        /// <summary>Null while art is outstanding.</summary>
        Sprite Icon { get; }

        /// <summary>The display-name entry in the definition's own String Table.</summary>
        string NameKey { get; }
    }
}
