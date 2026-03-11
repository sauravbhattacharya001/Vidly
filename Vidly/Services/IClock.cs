using System;

namespace Vidly.Services
{
    /// <summary>
    /// Abstracts the system clock so that time-dependent logic can be tested
    /// deterministically. Inject this instead of calling DateTime.Now directly.
    /// </summary>
    public interface IClock
    {
        /// <summary>Gets the current date and time.</summary>
        DateTime Now { get; }

        /// <summary>Gets the current date (time portion is midnight).</summary>
        DateTime Today { get; }
    }

    /// <summary>
    /// Production clock that delegates to <see cref="DateTime.Now"/>.
    /// Register as a singleton in your DI container.
    /// </summary>
    public class SystemClock : IClock
    {
        public DateTime Now => DateTime.Now;
        public DateTime Today => DateTime.Today;
    }
}
