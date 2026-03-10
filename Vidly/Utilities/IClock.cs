using System;

namespace Vidly.Utilities
{
    /// <summary>
    /// Abstraction over system clock to enable deterministic testing of time-dependent logic.
    /// </summary>
    public interface IClock
    {
        DateTime Now { get; }
        DateTime Today { get; }
    }

    /// <summary>
    /// Production clock that delegates to DateTime.Now.
    /// </summary>
    public class SystemClock : IClock
    {
        public DateTime Now => DateTime.Now;
        public DateTime Today => DateTime.Today;
    }
}
