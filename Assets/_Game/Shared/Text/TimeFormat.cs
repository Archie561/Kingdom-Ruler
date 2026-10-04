namespace KingdomRuler.Shared.Text
{
    /// <summary>
    /// How this game writes durations. One definition, so a countdown reads the same wherever
    /// it appears — on a screen and inside a popup describing the same timer.
    /// </summary>
    public static class TimeFormat
    {
        /// <summary>"m:ss" — the countdown format used by every timer in the game.</summary>
        public static string MinutesSeconds(int totalSeconds)
        {
            if (totalSeconds < 0) totalSeconds = 0;
            return $"{totalSeconds / 60}:{totalSeconds % 60:D2}";
        }
    }
}
