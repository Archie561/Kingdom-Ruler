using System;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace KingdomRuler.Systems.Localization
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
            // Localization loads asynchronously; until it is done, Resolve returns placeholders.
            // The SelectedLocaleChanged subscription is deliberately not here — see OnInitialisationComplete.
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

            // Gated on initialisation finishing, not on IsReady: with no locale configured
            // SelectedLocale stays null forever, and the game would never load its first scene.
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
        /// The <c>SelectedLocaleChanged</c> subscription must happen here: a handler attached
        /// before initialisation completes is never invoked. Unsubscribe-then-subscribe keeps this
        /// idempotent if initialisation ever re-runs.
        /// </remarks>
        private void OnInitialisationComplete()
        {
            _initialisationAnnounced = true;

            LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
            LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
            _subscribedToLocaleChanges = true;

            // Unity's default is a full sentence ("No translation found for…"), which overflows a
            // label and reads as a layout bug. Set on the database, not only in Resolve, so the
            // LocalizeStringEvent chrome — which never calls this service — gets it too.
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
        /// Missing text renders as the key in brackets — conspicuous on purpose, so it names the
        /// entry to add instead of looking like a blank layout bug.
        /// </summary>
        private static string Placeholder(string key) => $"[{key}]";
    }
}
