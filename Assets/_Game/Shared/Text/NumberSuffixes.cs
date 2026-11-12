namespace KingdomRuler.Shared.Text
{
    /// <summary>
    /// The localized suffix formats <see cref="NumberFormat.Abbreviate"/> writes big numbers with —
    /// <c>{0}K</c>, <c>{0} тис.</c> — one per magnitude, each with <c>{0}</c> where the number goes.
    /// </summary>
    /// <remarks>
    /// <para><b>It owns the keys, not the lookup.</b> The suffixes are cross-cutting words (Economy
    /// today; the currency header and Cities' gold costs next), so they live in <c>SharedTable</c>, and
    /// the table name and keys are declared here, beside the type whose text they are
    /// (<c>ARCHITECTURE.md</c> §2). <c>Shared</c> references nothing of ours, so it cannot resolve
    /// them itself: a Presenter resolves the four keys in <see cref="AllKeys"/> and builds this, then
    /// rebuilds it when the language changes.</para>
    ///
    /// <para>A format with no <c>{0}</c> — a missing entry comes back as <c>[key]</c> — still shows
    /// the number, followed by the format, so a gap is visible rather than swallowing the amount.</para>
    /// </remarks>
    public readonly struct NumberSuffixes
    {
        public const string StringTable = "SharedTable";

        public const string ThousandKey = "number.suffix.thousand";
        public const string MillionKey  = "number.suffix.million";
        public const string BillionKey  = "number.suffix.billion";
        public const string TrillionKey = "number.suffix.trillion";

        /// <summary>Every key, smallest magnitude first — the order of the constructor's arguments.</summary>
        public static readonly string[] AllKeys = { ThousandKey, MillionKey, BillionKey, TrillionKey };

        public readonly string Thousand;
        public readonly string Million;
        public readonly string Billion;
        public readonly string Trillion;

        public NumberSuffixes(string thousand, string million, string billion, string trillion)
        {
            Thousand = thousand;
            Million  = million;
            Billion  = billion;
            Trillion = trillion;
        }

        /// <summary>The format for 10^(3 × (<paramref name="magnitude"/> + 1)): 0 is thousands, 3 trillions.</summary>
        internal string FormatFor(int magnitude)
        {
            string format = magnitude switch
            {
                0 => Thousand,
                1 => Million,
                2 => Billion,
                _ => Trillion
            };
            if (string.IsNullOrEmpty(format)) return "{0}";
            return format.Contains("{0}") ? format : "{0}" + format;
        }
    }
}
