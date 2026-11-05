using UnityEngine;

namespace KingdomRuler.Systems.Audio
{
    /// <summary>Logs each call and plays nothing, until audio assets exist.</summary>
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
