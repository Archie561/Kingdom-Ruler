using System;

namespace KingdomRuler.Systems.Localization
{
    /// <summary>
    /// Resolves String Table entries to text in the active locale.
    /// </summary>
    /// <remarks>
    /// <para>A seam over Unity Localization so Presenters stay plain C# and test without booting
    /// the localization system; a fake returns the key.</para>
    ///
    /// <para>Entries are addressed by table + key rather than <c>LocalizedString</c> because every
    /// key in code is derived from data (a card id, an enum name). <c>LocalizedString</c> is used
    /// only by <c>LocalizeStringEvent</c> components on fixed UI chrome.</para>
    /// </remarks>
    public interface ILocalizationService
    {
        /// <summary>
        /// False until Unity Localization has finished its asynchronous startup. While false,
        /// <see cref="Resolve(string,string)"/> returns a placeholder rather than throwing.
        /// </summary>
        bool IsReady { get; }

        /// <summary>
        /// Text for <paramref name="key"/> in <paramref name="table"/>, in the active locale. Never
        /// null: a missing entry yields a visible <c>[key]</c> placeholder.
        /// </summary>
        string Resolve(string table, string key);

        /// <summary>
        /// As <see cref="Resolve(string,string)"/>, with Smart String arguments — for anything
        /// composed or inflected (plural forms differ between English and Ukrainian).
        /// </summary>
        string Resolve(string table, string key, params object[] args);

        /// <summary>
        /// Invokes <paramref name="onReady"/> once localization has started up — synchronously, if
        /// it already has.
        /// </summary>
        /// <remarks>
        /// <c>LocalizeStringEvent</c> components enabled before the tables load never receive a
        /// value, and <c>RefreshString()</c> cannot recover them. The bootstrap therefore shows no
        /// screen until this fires.
        /// </remarks>
        void WhenReady(Action onReady);

        /// <summary>
        /// Raised when the active locale changes, and once when localization finishes starting up.
        /// </summary>
        /// <remarks>
        /// <b>Every Presenter that calls <see cref="Resolve(string,string)"/> must subscribe and
        /// re-render, and unsubscribe in <c>Dispose</c>.</b> Only <c>LocalizeStringEvent</c>
        /// components switch language on their own; a resolved string is a plain copy. Missing this
        /// shows up as the chrome switching language while Presenter-built text does not.
        /// </remarks>
        event Action LocaleChanged;
    }
}
