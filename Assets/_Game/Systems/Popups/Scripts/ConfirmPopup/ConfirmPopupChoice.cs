namespace KingdomRuler.Systems.Popups.Confirm
{
    /// <summary>The buttons on a confirm popup.</summary>
    /// <remarks>
    /// Closing without pressing either — the backdrop, a <c>closeWhen</c> — is no choice at all:
    /// <c>Ask</c> gives <c>null</c>, which equals neither.
    /// </remarks>
    public enum ConfirmPopupChoice
    {
        Confirm,
        Cancel,
    }
}
