using System;

namespace KingdomRuler.Systems.Clock
{
    /// <summary>
    /// The game's time: what time it is, and when it has moved on.
    /// </summary>
    /// <remarks>
    /// Every time-driven system stores a UTC timestamp and settles against <see cref="UtcNow"/>,
    /// so one settle covers an absence of any length (<c>ARCHITECTURE.md</c> §4.5). An interface so
    /// tests can drive time by hand; <c>GameClock</c> is the real one.
    /// </remarks>
    public interface IClock
    {
        DateTime UtcNow { get; }

        /// <summary>
        /// Raised a few times a second with the current time. A time-driven system subscribes
        /// once, in its constructor, and settles its timers to the time it is given.
        /// </summary>
        event Action<DateTime> Ticked;
    }
}
