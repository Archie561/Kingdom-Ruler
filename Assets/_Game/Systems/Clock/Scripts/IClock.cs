using System;

namespace KingdomRuler.Systems.Clock
{
    /// <summary>
    /// Abstraction over system time. Enables deterministic testing of
    /// all time-dependent logic (offline accrual, timers, etc.).
    /// </summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
    }
}
