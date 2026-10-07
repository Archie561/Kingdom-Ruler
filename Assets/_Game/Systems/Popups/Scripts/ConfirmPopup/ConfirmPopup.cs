using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KingdomRuler.Systems.Popups.Confirm
{
    /// <summary>Asks the player to confirm or cancel something.</summary>
    /// <example>
    /// <code>
    /// var choice = await _popups.Create&lt;ConfirmPopup&gt;().Ask(new ConfirmPopupData(title, body));
    /// if (choice == ConfirmPopupChoice.Confirm) DoTheThing();
    /// </code>
    /// </example>
    public sealed class ConfirmPopup : Popup<ConfirmPopupData, ConfirmPopupChoice>
    {
        [Header("Content")]
        [SerializeField] private TextMeshProUGUI _titleLabel;
        [SerializeField] private TextMeshProUGUI _bodyLabel;

        [Header("Buttons")]
        [SerializeField] private Button _confirmButton;
        [SerializeField] private Button _cancelButton;

        private void Awake()
        {
            _confirmButton.onClick.AddListener(() => Choose(ConfirmPopupChoice.Confirm));
            _cancelButton.onClick.AddListener(() => Choose(ConfirmPopupChoice.Cancel));
        }

        protected override void Render(ConfirmPopupData data)
        {
            _titleLabel.SetText(data.Title);
            _bodyLabel.SetText(data.Body);
            _confirmButton.interactable = data.CanConfirm;
        }
    }
}
