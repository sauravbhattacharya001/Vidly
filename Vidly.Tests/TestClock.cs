using System;
using Vidly.Services;

namespace Vidly.Tests
{
    /// <summary>
    /// Test clock with settable time for deterministic testing.
    /// </summary>
    public class TestClock : IClock
    {
        private DateTime _now;

        public TestClock(DateTime? startTime = null)
        {
            _now = startTime ?? new DateTime(2025, 1, 1, 12, 0, 0);
        }

        public DateTime Now => _now;
        public DateTime Today => _now.Date;

        /// <summary>Advance time by the given duration.</summary>
        public void Advance(TimeSpan duration)
        {
            _now = _now.Add(duration);
        }

        /// <summary>Set time to an exact value.</summary>
        public void SetTime(DateTime time)
        {
            _now = time;
        }
    }
}
