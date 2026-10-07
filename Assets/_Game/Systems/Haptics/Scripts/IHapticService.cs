namespace KingdomRuler.Systems.Haptics
{
    /// <summary>
    /// Abstraction for haptic feedback. GDD §3 specifies:
    /// - Light impact on swipe-decision, purchase confirmation, level-up
    /// - Do not overuse elsewhere
    /// </summary>
    public interface IHapticService
    {
        void TriggerLight();
        void TriggerMedium();
    }
}
