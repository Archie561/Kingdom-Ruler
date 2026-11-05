using System;
using UnityEngine;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Systems.Popups;

namespace KingdomRuler.Modules.Laws.UI
{
    /// <summary>
    /// Every characteristic in a large row with its buy-up button. Opened by tapping the dial panel.
    /// </summary>
    /// <remarks>
    /// The choice is the characteristic whose buy-up button was pressed. There is no close button:
    /// a tap outside closes it, which gives the caller <c>null</c>.
    /// </remarks>
    public sealed class CharacteristicsPopup : Popup<CharacteristicsPopupData, CharacteristicType>
    {
        [Tooltip("One row per characteristic. Matched by each row's own Type, so order does not matter.")]
        [SerializeField] private CharacteristicRowView[] _rows;

        private void Awake()
        {
            foreach (var row in _rows)
                row.BuyPressed += Choose;

            CheckRowsCoverEveryCharacteristic();
        }

        protected override void Render(CharacteristicsPopupData data)
        {
            foreach (var row in _rows)
                foreach (var rowData in data.Rows)
                    if (rowData.Type == row.Type) row.Render(rowData);
        }

        private void CheckRowsCoverEveryCharacteristic()
        {
            foreach (CharacteristicType type in Enum.GetValues(typeof(CharacteristicType)))
            {
                int count = 0;
                foreach (var row in _rows)
                    if (row.Type == type) count++;

                if (count != 1)
                    Debug.LogError($"[CharacteristicsPopup] {count} rows show {type}; there must be exactly one.", this);
            }
        }
    }
}
