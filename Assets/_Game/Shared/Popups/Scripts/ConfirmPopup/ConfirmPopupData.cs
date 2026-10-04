namespace KingdomRuler.Shared.Popups.Confirm
{
    /// <summary>
    /// What a confirm popup shows. Text arrives already localized; the Confirm and Cancel button
    /// labels are fixed and live on the prefab.
    /// </summary>
    public readonly struct ConfirmPopupData
    {
        public readonly string Title;
        public readonly string Body;

        /// <summary>Whether Confirm can be pressed right now — typically "can the player afford it".</summary>
        public readonly bool CanConfirm;

        public ConfirmPopupData(string title, string body, bool canConfirm = true)
        {
            Title      = title;
            Body       = body;
            CanConfirm = canConfirm;
        }
    }
}
