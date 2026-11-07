using TMPro;
using UnityEngine;
using UnityEngine.UI;
using KingdomRuler.Systems.Popups;

namespace KingdomRuler.Modules.Trade.UI
{
    /// <summary>
    /// The confirmation every offer opens (GDD §7): what is given and received, a line per resource
    /// the player cannot pay (Accept disabled), and a line per resource that will lose units to a
    /// full warehouse (Accept still enabled).
    /// </summary>
    /// <remarks>No close button: a tap outside closes it, which gives the caller <c>null</c>.</remarks>
    public sealed class TradeOfferPopup : Popup<TradeOfferPopupData, TradeOfferPopupChoice>
    {
        [Header("Terms")]
        [Tooltip("Three slots: an offer gives 2–3 resources.")]
        [SerializeField] private ResourceAmountView[] _give;
        [Tooltip("Three slots: an offer receives 2–3 resources.")]
        [SerializeField] private ResourceAmountView[] _receive;

        [Header("Messages")]
        [SerializeField] private TextMeshProUGUI _blockersLabel;
        [SerializeField] private TextMeshProUGUI _warningsLabel;

        [Header("Buttons")]
        [SerializeField] private Button _acceptButton;
        [SerializeField] private Button _cancelButton;

        private void Awake()
        {
            _acceptButton.onClick.AddListener(() => Choose(TradeOfferPopupChoice.Accept));
            _cancelButton.onClick.AddListener(() => Choose(TradeOfferPopupChoice.Cancel));
        }

        protected override void Render(TradeOfferPopupData data)
        {
            ResourceAmountView.ShowAll(_give, data.Give);
            ResourceAmountView.ShowAll(_receive, data.Receive);

            ShowMessage(_blockersLabel, data.Blockers);
            ShowMessage(_warningsLabel, data.Warnings);

            _acceptButton.interactable = data.CanAccept;
        }

        private static void ShowMessage(TextMeshProUGUI label, string text)
        {
            bool any = !string.IsNullOrEmpty(text);
            label.gameObject.SetActive(any);
            if (any) label.SetText(text);
        }
    }
}
