using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Laws.UI
{
    /// <summary>
    /// One row of the characteristics popup: icon, name, level, points / required with a progress
    /// bar, and the buy-up button with its price.
    /// </summary>
    public sealed class CharacteristicRowView : MonoBehaviour
    {
        [Tooltip("The characteristic this row shows. Set per instance.")]
        [SerializeField] private CharacteristicType _type;

        [Header("Display")]
        [SerializeField] private Image           _icon;
        [SerializeField] private TextMeshProUGUI _nameLabel;
        [SerializeField] private TextMeshProUGUI _levelLabel;
        [SerializeField] private TextMeshProUGUI _pointsLabel;
        [Tooltip("Image type Filled, fillMethod Horizontal.")]
        [SerializeField] private Image           _progressBar;

        [Header("Buy-up")]
        [SerializeField] private Button          _buyButton;
        [SerializeField] private TextMeshProUGUI _priceLabel;

        [Header("Animation")]
        [SerializeField] private float _barFillDuration       = 1.0f;
        [SerializeField] private float _levelUpTopOffDuration = 0.5f;

        private LevelFill _fill;

        public CharacteristicType Type => _type;

        /// <summary>Raised when the player taps this row's buy-up button.</summary>
        public event Action<CharacteristicType> BuyPressed;

        private void Awake()
        {
            _fill = new LevelFill(_progressBar, _barFillDuration, _levelUpTopOffDuration);
            _buyButton.onClick.AddListener(() => BuyPressed?.Invoke(_type));
        }

        private void OnDestroy() => _fill?.Kill();

        public void Render(CharacteristicRowViewData data)
        {
            // An Image without a sprite draws a white box, so hide it instead.
            _icon.sprite  = data.Icon;
            _icon.enabled = data.Icon != null;

            _nameLabel.SetText(data.Name);
            _levelLabel.SetText(data.LevelText);
            _pointsLabel.SetText($"{data.Points} / {data.PointsRequired}");
            _priceLabel.SetText(data.BuyUpCost.ToString());

            _fill.Render(data.Level, data.Progress);
        }
    }
}
