using System;

namespace Vidly.Services
{
    /// <summary>
    /// Abstraction over system clock to enable deterministic testing
    /// of time-dependent logic. Inject this instead of using DateTime.Now directly.
    /// </summary>
    public interface IClock
    {
        /// <summary>Gets the current local date and time.</summary>
        DateTime Now { get; }

        /// <summary>Gets the current date (time component is midnight).</summary>
        DateTime Today { get; }
    }

    /// <summary>
    /// Production clock that delegates to <see cref="DateTime.Now"/>.
    /// Register as singleton in DI.
    /// </summary>
    public class SystemClock : IClock
    {
        public DateTime Now => DateTime.Now;
        public DateTime Today => DateTime.Today;
    }

    /// <summary>
    /// Test clock with a settable time. Use in unit tests for deterministic behavior.
    /// </summary>
    public class TestClock : IClock
    {
        public TestClock(DateTime? startTime = null)
        {
            Now = startTime ?? new DateTime(2026, 1, 15, 12, 0, 0);
        }

        public DateTime Now { get; set; }
        public DateTime Today => Now.Date;

        /// <summary>Advance the clock by the given time span.</summary>
        public void Advance(TimeSpan duration)
        {
            Now = Now.Add(duration);
        }
    }
}
