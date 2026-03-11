using System;
using Vidly.Services;

namespace Vidly.Tests
{
    /// <summary>
    /// Test double for <see cref="IClock"/> that allows controlling time
    /// in unit tests. Set <see cref="Now"/> directly or use <see cref="Advance"/>.
    /// </summary>
    public class TestClock : IClock
    {
        public TestClock() : this(new DateTime(2025, 6, 15, 12, 0, 0)) { }

        public TestClock(DateTime initialTime)
        {
            Now = initialTime;
        }

        public DateTime Now { get; set; }

        public DateTime Today => Now.Date;

        /// <summary>Advance the clock by the specified duration.</summary>
        public void Advance(TimeSpan duration) => Now = Now.Add(duration);
    }
}
