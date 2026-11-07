using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KingdomRuler.Modules.Trade.UI
{
    /// <summary>One "12 Stone" line, in an offer row and in the offer confirmation.</summary>
    public sealed class ResourceAmountView : MonoBehaviour
    {
        [SerializeField] private Image           _icon;
        [SerializeField] private TextMeshProUGUI _amountLabel;
        [SerializeField] private TextMeshProUGUI _nameLabel;

        public void Show(ResourceAmountViewData data)
        {
            // An Image without a sprite draws a white box, so hide it instead.
            _icon.sprite  = data.Icon;
            _icon.enabled = data.Icon != null;

            _amountLabel.SetText(data.Units.ToString("N0"));
            _nameLabel.SetText(data.Name);
            gameObject.SetActive(true);
        }

        public void Hide() => gameObject.SetActive(false);

        /// <summary>Show the first lines of <paramref name="data"/> in <paramref name="slots"/> and hide the slots left over.</summary>
        public static void ShowAll(ResourceAmountView[] slots, ResourceAmountViewData[] data)
        {
            int count = data == null ? 0 : data.Length;
            for (int i = 0; i < slots.Length; i++)
            {
                if (i < count) slots[i].Show(data[i]);
                else           slots[i].Hide();
            }

            if (count > slots.Length)
                Debug.LogError($"[ResourceAmountView] {count} lines for {slots.Length} slots; the rest are not shown.", slots[0]);
        }
    }
}
