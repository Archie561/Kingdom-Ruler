namespace KingdomRuler.Systems.Purchasing
{
    /// <summary>
    /// Result of an IAP purchase attempt.
    /// </summary>
    public sealed class PurchaseResult
    {
        public bool Success { get; }
        public int CrystalsAwarded { get; }
        public string ErrorMessage { get; }

        private PurchaseResult(bool success, int crystalsAwarded, string errorMessage)
        {
            Success = success;
            CrystalsAwarded = crystalsAwarded;
            ErrorMessage = errorMessage;
        }

        public static PurchaseResult Succeeded(int crystalsAwarded)
            => new PurchaseResult(true, crystalsAwarded, null);

        public static PurchaseResult Failed(string errorMessage)
            => new PurchaseResult(false, 0, errorMessage);
    }
}
