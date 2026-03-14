using System;

namespace Vidly.Services
{
    /// <summary>
    /// Abstraction over system clock to enable deterministic testing
    /// of time-dependent logic.
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
}
