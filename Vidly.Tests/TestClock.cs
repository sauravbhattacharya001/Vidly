using System;
using Vidly.Utilities;

namespace Vidly.Tests
{
    /// <summary>
    /// Test clock with settable time for deterministic testing of time-dependent logic.
    /// </summary>
    public class TestClock : IClock
    {
        public DateTime Now { get; set; }
        public DateTime Today => Now.Date;

        public TestClock() : this(new DateTime(2026, 1, 15, 12, 0, 0)) { }

        public TestClock(DateTime now)
        {
            Now = now;
        }

        /// <summary>Advance the clock by the specified duration.</summary>
        public void Advance(TimeSpan duration)
        {
            Now = Now.Add(duration);
        }

        /// <summary>Advance the clock by the specified number of days.</summary>
        public void AdvanceDays(int days)
        {
            Now = Now.AddDays(days);
        }

        /// <summary>Advance the clock by the specified number of hours.</summary>
        public void AdvanceHours(int hours)
        {
            Now = Now.AddHours(hours);
        }
    }
}
