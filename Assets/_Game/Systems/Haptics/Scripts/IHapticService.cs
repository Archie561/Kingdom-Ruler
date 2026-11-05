namespace KingdomRuler.Systems.Haptics
{
    /// <summary>
    /// Haptic feedback. Light impact only on swipe decisions, purchase confirmations and
    /// level-ups (<c>GDD.md</c> §3) — anywhere else it becomes noise.
    /// </summary>
    public interface IHapticService
    {
        void TriggerLight();
        void TriggerMedium();
    }
}
