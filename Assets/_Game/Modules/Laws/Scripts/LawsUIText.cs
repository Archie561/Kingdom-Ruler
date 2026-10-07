namespace KingdomRuler.Modules.Laws
{
    /// <summary>
    /// Laws screen text that is built in code and has no data asset behind it — popup copy
    /// with numbers in it, which a <c>LocalizeStringEvent</c> cannot produce.
    /// </summary>
    /// <remarks>
    /// <para>Public and a type of its own, not nested in the Presenter: <c>LawsContentTests</c>
    /// lives in the separate EditMode test assembly and imports these, which is the whole point —
    /// one declaration, checked against the real String Table
    /// (<c>ARCHITECTURE.md</c> §2).</para>
    ///
    /// <para>Fixed labels that never change at runtime are <b>not</b> here; those are
    /// <c>LocalizeStringEvent</c> components on the prefab.</para>
    /// </remarks>
    public static class LawsUIText
    {
        public const string StringTable = "LawsUITable";

        /// <summary>Title of the "spend crystals to refill" dialog.</summary>
        public const string RefillTitle = "ui.refill.title";

        /// <summary>
        /// Body of the refill dialog. Smart String: {0} = crystal cost,
        /// {1} = time until every law has returned on its own.
        /// </summary>
        public const string RefillBody = "ui.refill.body";
    }
}
