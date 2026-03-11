using System;

namespace Vidly.Services
{
    /// <summary>
    /// Abstraction over system time, enabling deterministic testing
    /// of time-dependent logic (billing, promotions, expiry, etc.).
    /// </summary>
    public interface IClock
    {
        /// <summary>Gets the current local date and time.</summary>
        DateTime Now { get; }

        /// <summary>Gets the current date (time portion is midnight).</summary>
        DateTime Today { get; }
    }

    /// <summary>
    /// Production implementation that delegates to <see cref="DateTime.Now"/>.
    /// </summary>
    public class SystemClock : IClock
    {
        public DateTime Now => DateTime.Now;
        public DateTime Today => DateTime.Today;
    }
}
