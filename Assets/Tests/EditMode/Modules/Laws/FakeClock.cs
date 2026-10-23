using System;
using KingdomRuler.Systems.Clock;

namespace KingdomRuler.Tests.EditMode.Modules.Laws
{
    public sealed class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; } = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public void Advance(TimeSpan duration) => UtcNow += duration;

        public event Action<DateTime> Ticked;

        /// <summary>What GameClock does four times a second.</summary>
        public void Tick() => Ticked?.Invoke(UtcNow);
    }
}
