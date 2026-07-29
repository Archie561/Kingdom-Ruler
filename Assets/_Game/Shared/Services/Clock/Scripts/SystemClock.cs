using System;

namespace KingdomRuler.Shared.Services
{
    /// <summary>
    /// Production IClock implementation. Returns real system time.
    /// </summary>
    public sealed class SystemClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
