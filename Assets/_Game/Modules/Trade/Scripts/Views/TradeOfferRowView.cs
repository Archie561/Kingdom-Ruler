using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using KingdomRuler.Modules.Trade.Presenters;

namespace KingdomRuler.Modules.Trade.Views
{
    /// <summary>
    /// One row in the offer list: what the player gives, what they get, and whether anything is
    /// wrong with taking it.
    /// </summary>
    /// <remarks>
    /// <para><b>The row is always tappable</b>, even when the trade cannot go through. GDD §7 is
    /// explicit that an offer is never silently disabled — the row styles itself as unaffordable
    /// and the confirmation panel explains why. Do not bind <c>interactable</c> to
    /// <see cref="TradeOfferDisplayData.CanAccept"/>.</para>
    ///
    /// <para>Greybox rendering: each side is one label listing "12 Stone, 8 Wood", built from the
    /// already-localized name and amount on each line. Joining them is layout, not translation.
    /// The trigger to replace this with a per-line sub-view is resource <b>icons</b> — those need
    /// one <c>Image</c> each and cannot be joined into a string.</para>
    /// </remarks>
    public sealed class TradeOfferRowView : MonoBehaviour
    {
        [SerializeField] private Button _button;
        [SerializeField] private TextMeshProUGUI _giveLabel;
        [SerializeField] private TextMeshProUGUI _receiveLabel;

        [Tooltip("Shown when accepting would lose units to a full warehouse. The offer is still " +
                 "acceptable — this is a warning, not a blocker.")]
        [SerializeField] private GameObject _overflowBadge;

        [Header("Look")]
        [SerializeField] private Image _background;
        [SerializeField] private Color _affordableColor   = new(0.16f, 0.22f, 0.28f, 1f);
        [SerializeField] private Color _unaffordableColor = new(0.22f, 0.16f, 0.16f, 1f);

        private readonly StringBuilder _builder = new();
        private string _offerId;

        /// <summary>The offer this row currently shows. Empty when the row is unused.</summary>
        public string OfferId => _offerId;

        public event Action<string> OnPressed;

        private void Awake()
        {
            if (_button != null) _button.onClick.AddListener(HandlePressed);
        }

        private void OnDestroy()
        {
            if (_button != null) _button.onClick.RemoveListener(HandlePressed);
        }

        /// <summary>Show an offer.</summary>
        public void Bind(TradeOfferDisplayData data)
        {
            _offerId = data.OfferId;

            if (_giveLabel    != null) _giveLabel.SetText(Join(data.Give));
            if (_receiveLabel != null) _receiveLabel.SetText(Join(data.Receive));

            if (_overflowBadge != null) _overflowBadge.SetActive(data.HasOverflowWarning);

            // Styling only — the button stays interactable either way.
            if (_background != null)
                _background.color = data.CanAccept ? _affordableColor : _unaffordableColor;

            gameObject.SetActive(true);
        }

        /// <summary>Park this row when the list is shorter than the pool of rows.</summary>
        public void Hide()
        {
            _offerId = null;
            gameObject.SetActive(false);
        }

        private string Join(System.Collections.Generic.IReadOnlyList<TradeResourceLineData> lines)
        {
            _builder.Clear();
            if (lines == null) return string.Empty;

            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) _builder.Append(", ");
                _builder.Append(lines[i].AmountText).Append(' ').Append(lines[i].Name);
            }

            return _builder.ToString();
        }

        private void HandlePressed()
        {
            if (!string.IsNullOrEmpty(_offerId)) OnPressed?.Invoke(_offerId);
        }
    }
}
