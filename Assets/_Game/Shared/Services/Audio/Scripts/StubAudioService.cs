using UnityEngine;

namespace KingdomRuler.Shared.Services
{
    /// <summary>
    /// Stub audio service that logs calls but plays nothing.
    /// Replaced with a real implementation once audio assets exist.
    /// </summary>
    public sealed class StubAudioService : IAudioService
    {
        public void PlaySfx(string sfxId)
        {
            Debug.Log($"[StubAudioService] PlaySfx: {sfxId}");
        }

        public void PlayMusic(string musicId)
        {
            Debug.Log($"[StubAudioService] PlayMusic: {musicId}");
        }

        public void StopMusic()
        {
            Debug.Log("[StubAudioService] StopMusic");
        }
    }
}
