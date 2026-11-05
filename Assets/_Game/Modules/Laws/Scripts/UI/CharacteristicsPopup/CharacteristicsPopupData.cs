using System;

namespace KingdomRuler.Modules.Laws.UI
{
    /// <summary>What <see cref="CharacteristicsPopup"/> draws: one row per characteristic.</summary>
    /// <remarks>
    /// Compared row by row: the array is rebuilt on every read, so the default comparison (by
    /// reference) would redraw the popup several times a second.
    /// </remarks>
    public readonly struct CharacteristicsPopupData : IEquatable<CharacteristicsPopupData>
    {
        public readonly CharacteristicRowViewData[] Rows;

        public CharacteristicsPopupData(CharacteristicRowViewData[] rows) => Rows = rows;

        public bool Equals(CharacteristicsPopupData other)
        {
            if (ReferenceEquals(Rows, other.Rows)) return true;
            if (Rows == null || other.Rows == null || Rows.Length != other.Rows.Length) return false;

            for (int i = 0; i < Rows.Length; i++)
                if (!Rows[i].Equals(other.Rows[i])) return false;
            return true;
        }

        public override bool Equals(object obj) => obj is CharacteristicsPopupData other && Equals(other);

        public override int GetHashCode() => Rows == null ? 0 : Rows.Length;
    }
}
