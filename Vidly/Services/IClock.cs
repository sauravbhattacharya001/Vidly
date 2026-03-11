namespace Vidly.Services
{
    /// <summary>
    /// Abstraction over system clock for deterministic testing of time-dependent logic.
    /// </summary>
    public interface IClock
    {
        /// <summary>Gets the current date and time.</summary>
        DateTime Now { get; }

        /// <summary>Gets the current date (time component is midnight).</summary>
        DateTime Today { get; }
    }

    /// <summary>
    /// Production clock that delegates to <see cref="DateTime.Now"/>.
    /// </summary>
    public class SystemClock : IClock
    {
        public DateTime Now => DateTime.Now;
        public DateTime Today => DateTime.Today;
    }

    /// <summary>
    /// Test clock with settable time for deterministic unit tests.
    /// </summary>
    public class TestClock : IClock
    {
        private DateTime _now;

        public TestClock(DateTime startTime)
        {
            _now = startTime;
        }

        public TestClock() : this(new DateTime(2026, 1, 15, 12, 0, 0)) { }

        public DateTime Now => _now;
        public DateTime Today => _now.Date;

        /// <summary>Set the clock to a specific time.</summary>
        public void SetNow(DateTime value) => _now = value;

        /// <summary>Advance the clock by the given time span.</summary>
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
