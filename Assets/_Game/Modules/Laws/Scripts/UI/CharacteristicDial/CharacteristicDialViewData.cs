using UnityEngine;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Laws.UI
{
    /// <summary>What <see cref="CharacteristicDialView"/> draws for one characteristic.</summary>
    public readonly struct CharacteristicDialViewData
    {
        public readonly CharacteristicType Type;

        /// <summary>Null while the characteristic has no art yet; the dial hides the slot.</summary>
        public readonly Sprite Icon;

        public readonly int Level;

        /// <summary>Progress through the current level, 0..1.</summary>
        public readonly float Progress;

        public CharacteristicDialViewData(CharacteristicType type, Sprite icon, int level, float progress)
        {
            Type     = type;
            Icon     = icon;
            Level    = level;
            Progress = progress;
        }
    }
}
