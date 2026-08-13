using System;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace KingdomRuler.Shared.Services
{
    /// <summary>
    /// <see cref="ILocalizationService"/> backed by Unity Localization.
    /// </summary>
    public sealed class UnityLocalizationService : ILocalizationService, IDisposable
    {
        private bool   _initialisationAnnounced;
        private bool   _subscribedToLocaleChanges;
        private Action _pendingReadyCallbacks;

        public UnityLocalizationService()
        {
            // Localization loads asynchronously, so anything constructed at boot — this service
            // included — runs before a single table exists. Resolve to placeholders until then.
            //
            // Note what is deliberately NOT here: the SelectedLocaleChanged subscription. See
            // OnInitialisationComplete.
            var init = LocalizationSettings.InitializationOperation;
            if (init.IsDone) OnInitialisationComplete();
            else init.Completed += _ => OnInitialisationComplete();
        }

        public bool IsReady =>
            _initialisationAnnounced && LocalizationSettings.SelectedLocale != null;

        public event Action LocaleChanged;

        public string Resolve(string table, string key) => Resolve(table, key, null);

        public string Resolve(string table, string key, params object[] args)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (!IsReady) return Placeholder(key);

            try
            {
                var text = args == null || args.Length == 0
                    ? LocalizationSettings.StringDatabase.GetLocalizedString(table, key)
                    : LocalizationSettings.StringDatabase.GetLocalizedString(table, key, args);

                return string.IsNullOrEmpty(text) ? Placeholder(key) : text;
            }
            catch (Exception ex)
            {
                // A missing table or entry must not take down the screen that asked for it.
                Debug.LogWarning(
                    $"[UnityLocalizationService] Could not resolve '{key}' in table '{table}' " +
                    $"({ex.GetType().Name}: {ex.Message}). Showing a placeholder.");
                return Placeholder(key);
            }
        }

        public void WhenReady(Action onReady)
        {
            if (onReady == null) return;

            // Gated on "initialisation finished", NOT on IsReady. A project with no locales
            // configured leaves SelectedLocale null forever, and if the boot gate waited on
            // that the game would never load its first scene — a black screen is a far worse
            // failure than untranslated text.
            if (_initialisationAnnounced) onReady();
            else _pendingReadyCallbacks += onReady;
        }

        public void Dispose()
        {
            if (_subscribedToLocaleChanges)
            {
                LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
                _subscribedToLocaleChanges = false;
            }
            _pendingReadyCallbacks = null;
        }

        /// <summary>
        /// Marks the service usable and starts listening for locale changes.
        /// </summary>
        /// <remarks>
        /// The subscription lives here rather than in the constructor because of a measured
        /// failure: a handler attached to <c>SelectedLocaleChanged</c> before initialisation
        /// had completed was never invoked afterwards, while one attached after it was invoked
        /// normally — same static event, same instance. Subscribing at construction therefore
        /// looked correct and silently did nothing, and the symptom was remote from the cause:
        /// switching language updated the <c>LocalizeStringEvent</c> chrome (which listens on
        /// its own) while every Presenter-built string stayed in the previous language.
        ///
        /// Unsubscribe-then-subscribe keeps this idempotent if initialisation ever re-runs.
        /// </remarks>
        private void OnInitialisationComplete()
        {
            _initialisationAnnounced = true;

            LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
            LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
            _subscribedToLocaleChanges = true;

            // Unity's own default here is a full sentence — "No translation found for 'x' in
            // y" — which is fine in a console and wrong on a phone: dropped into a card title
            // it wraps, overflows, and reads as a layout bug rather than a missing entry.
            // Setting it on the database rather than only in Resolve() matters, because the
            // LocalizeStringEvent components on the static chrome never call this service and
            // would otherwise still render the sentence.
            LocalizationSettings.StringDatabase.NoTranslationFoundMessage = MissingEntryFormat;

            // Cleared before invoking so a callback that re-enters WhenReady runs immediately
            // instead of being queued onto a list that is about to be discarded.
            var callbacks = _pendingReadyCallbacks;
            _pendingReadyCallbacks = null;
            callbacks?.Invoke();

            LocaleChanged?.Invoke();
        }

        private void OnSelectedLocaleChanged(Locale _) => LocaleChanged?.Invoke();

        /// <summary>
        /// Smart String form of <see cref="Placeholder"/>, for Unity's own missing-entry path.
        /// </summary>
        private const string MissingEntryFormat = "[{key}]";

        /// <summary>
        /// Missing text renders as the key in brackets. Deliberately conspicuous: blank
        /// labels look like a layout bug and get chased in the wrong place, whereas
        /// "[1.title]" names the entry that needs adding.
        /// </summary>
        private static string Placeholder(string key) => $"[{key}]";
    }
}
