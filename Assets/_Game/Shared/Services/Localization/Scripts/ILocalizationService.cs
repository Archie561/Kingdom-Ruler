using System;

namespace KingdomRuler.Shared.Services
{
    /// <summary>
    /// Resolves String Table entries to text in the active locale.
    /// </summary>
    /// <remarks>
    /// <para>A seam over Unity Localization, for the same reason <see cref="IClock"/> and
    /// <see cref="ISaveService"/> are seams: Presenters are plain C# and unit-tested without
    /// the Editor. Calling <c>LocalizationSettings</c> directly would force every Presenter
    /// test to boot the localization system and select a locale — slow and flaky. A fake
    /// returns the key instead, and the tests stay as they are.</para>
    ///
    /// <para>Entries are addressed by table + key rather than by <c>LocalizedString</c>
    /// because every key in the game is <b>derived from data</b> — a card's id, a
    /// characteristic's enum name — so there is nothing for a designer to wire and nothing to
    /// mistype. <c>LocalizedString</c> is still used, but only inside
    /// <c>LocalizeStringEvent</c> components for fixed UI chrome, which resolve themselves.</para>
    /// </remarks>
    public interface ILocalizationService
    {
        /// <summary>
        /// False until Unity Localization has finished its asynchronous startup. While false,
        /// <see cref="Resolve(string,string)"/> returns a readable fallback rather than
        /// throwing, and <see cref="LocaleChanged"/> fires once loading completes.
        /// </summary>
        bool IsReady { get; }

        /// <summary>
        /// Text for <paramref name="key"/> in <paramref name="table"/>, in the active locale.
        /// Never returns null: a missing entry yields a visible placeholder so the gap shows
        /// up on screen instead of as blank space.
        /// </summary>
        string Resolve(string table, string key);

        /// <summary>
        /// As <see cref="Resolve(string,string)"/>, with Smart String arguments — needed for
        /// anything composed or inflected (plural forms differ between English and Ukrainian).
        /// </summary>
        string Resolve(string table, string key, params object[] args);

        /// <summary>
        /// Invokes <paramref name="onReady"/> once localization has finished starting up —
        /// synchronously, if it already has.
        /// </summary>
        /// <remarks>
        /// This exists because <c>LocalizeStringEvent</c> components are <b>not</b> resilient to
        /// being enabled first: one that subscribes before the tables load never receives a
        /// value and keeps whatever text the prefab was authored with, permanently. Refreshing
        /// it afterwards does not help — its loading handle is never valid, so
        /// <c>RefreshString()</c> is a silent no-op. The only reliable fix is to not show the
        /// screen until localization is up, which is what the bootstrap uses this for.
        /// </remarks>
        void WhenReady(Action onReady);

        /// <summary>
        /// Raised when the active locale changes, and once when localization finishes
        /// initialising.
        /// </summary>
        /// <remarks>
        /// <para><b>Every Presenter that calls <see cref="Resolve(string,string)"/> must
        /// subscribe to this and re-render, and unsubscribe in <c>Dispose</c>.</b> Language
        /// switching is only automatic for <c>LocalizeStringEvent</c> components, which
        /// refresh themselves; a string this service returned is a plain copy that nothing
        /// updates afterwards.</para>
        ///
        /// <para>Forgetting it fails in a way that misdirects: the screen's fixed chrome
        /// switches language correctly while everything the Presenter built stays in the old
        /// one, which looks like a bug in the Presenter's data rather than a missing
        /// subscription. <c>LawsPresenter</c> is the worked example.</para>
        /// </remarks>
        event Action LocaleChanged;
    }
}
