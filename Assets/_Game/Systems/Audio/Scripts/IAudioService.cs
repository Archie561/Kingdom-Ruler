namespace KingdomRuler.Systems.Audio
{
    /// <summary>
    /// Abstraction for playing sound effects and music.
    /// </summary>
    public interface IAudioService
    {
        void PlaySfx(string sfxId);
        void PlayMusic(string musicId);
        void StopMusic();
    }
}
