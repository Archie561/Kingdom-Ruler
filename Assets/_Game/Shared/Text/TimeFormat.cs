namespace KingdomRuler.Shared.Text
{
    /// <summary>How this game writes durations, in one place, so a countdown reads the same everywhere.</summary>
    public static class TimeFormat
    {
        /// <summary>"m:ss" — the countdown format used by every timer in the game.</summary>
        public static string MinutesSeconds(int totalSeconds)
        {
            if (totalSeconds < 0) totalSeconds = 0;
            return $"{totalSeconds / 60}:{totalSeconds % 60:D2}";
        }

        /// <summary>"h:mm:ss" — for waits that can run past an hour, such as a warehouse filling.</summary>
        public static string HoursMinutesSeconds(int totalSeconds)
        {
            if (totalSeconds < 0) totalSeconds = 0;
            return $"{totalSeconds / 3600}:{totalSeconds / 60 % 60:D2}:{totalSeconds % 60:D2}";
        }
    }
}
