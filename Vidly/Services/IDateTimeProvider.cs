using System;

namespace Vidly.Services
{
    /// <summary>
    /// Abstracts <see cref="DateTime.Now"/> / <see cref="DateTime.UtcNow"/>
    /// so services are unit-testable with deterministic time.
    /// </summary>
    public interface IDateTimeProvider
    {
        DateTime Now { get; }
        DateTime UtcNow { get; }
        DateTime Today { get; }
    }

    /// <summary>Production implementation that delegates to <see cref="DateTime"/>.</summary>
    public class SystemDateTimeProvider : IDateTimeProvider
    {
        public DateTime Now => DateTime.Now;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime Today => DateTime.Today;
    }
}
