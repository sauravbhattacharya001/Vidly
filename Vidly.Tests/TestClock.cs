using System;
using Vidly.Services;

namespace Vidly.Tests
{
    /// <summary>
    /// Test clock with a settable time for deterministic testing.
    /// </summary>
    public class TestClock : IClock
    {
        public TestClock(DateTime? initialTime = null)
        {
            Now = initialTime ?? new DateTime(2025, 6, 15, 12, 0, 0);
        }

        public DateTime Now { get; set; }
        public DateTime Today => Now.Date;

        /// <summary>Advance the clock by the given amount.</summary>
        public void Advance(TimeSpan amount) => Now = Now.Add(amount);

        /// <summary>Advance the clock by the given number of days.</summary>
        public void AdvanceDays(int days) => Now = Now.AddDays(days);

        /// <summary>Advance the clock by the given number of hours.</summary>
        public void AdvanceHours(int hours) => Now = Now.AddHours(hours);
    }
}
