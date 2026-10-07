using System;
using System.Collections.Generic;
using KingdomRuler.Systems.Audio;
using KingdomRuler.Systems.Haptics;
using KingdomRuler.Systems.Localization;

namespace KingdomRuler.Tests.EditMode.Systems
{
    // ── Shared service fakes ──────────────────────────────────────────────────────
    // Promoted here from the Laws presenter tests once Trade needed the identical ones.
    // Copying them per module is how you end up with two that drift; TestCharacteristics in
    // Systems/Ledger already sets this precedent. FakeClock is deliberately NOT here — each
    // module's test namespace declares its own and they do not collide.

    public sealed class FakeAudioService : IAudioService
    {
        public readonly List<string> PlayedSfxIds = new();
        public void PlaySfx(string sfxId)    => PlayedSfxIds.Add(sfxId);
        public void PlayMusic(string musicId) { }
        public void StopMusic()              { }
    }

    public sealed class FakeHapticService : IHapticService
    {
        public int LightTriggerCount;
        public int MediumTriggerCount;
        public void TriggerLight()  => LightTriggerCount++;
        public void TriggerMedium() => MediumTriggerCount++;
    }

    /// <summary>
    /// Returns "table:key" so tests can assert exactly which entry the Presenter asked for,
    /// without booting Unity Localization. Overrides let a test pin real text where the
    /// wording matters.
    /// </summary>
    public sealed class FakeLocalizationService : ILocalizationService
    {
        public readonly System.Collections.Generic.List<string> Requested = new();
        public readonly System.Collections.Generic.Dictionary<string, string> Overrides = new();

        public bool IsReady { get; set; } = true;
        public event Action LocaleChanged;

        public string Resolve(string table, string key) => Resolve(table, key, null);

        public string Resolve(string table, string key, params object[] args)
        {
            string id = table + ":" + key;
            Requested.Add(id);
            if (Overrides.TryGetValue(id, out var text)) return text;
            // Smart String stand-in: append the args so composition is still observable.
            if (args != null && args.Length > 0) return id + "(" + string.Join(",", args) + ")";
            return id;
        }

        public void WhenReady(Action onReady)
        {
            if (onReady == null) return;
            if (IsReady) onReady();
            else _pendingReady += onReady;
        }

        private Action _pendingReady;

        /// <summary>Simulate localization finishing its asynchronous startup.</summary>
        public void BecomeReady()
        {
            IsReady = true;
            var callbacks = _pendingReady;
            _pendingReady = null;
            callbacks?.Invoke();
            LocaleChanged?.Invoke();
        }

        /// <summary>Simulate the player switching language.</summary>
        public void RaiseLocaleChanged() => LocaleChanged?.Invoke();
    }
}
