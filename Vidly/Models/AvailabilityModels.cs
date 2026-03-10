using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// Availability status for a single movie.
    /// </summary>
    public class MovieAvailability
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public Genre? Genre { get; set; }
        public int? Rating { get; set; }

        /// <summary>Whether the movie is currently available for rental.</summary>
        public bool IsAvailable { get; set; }

        /// <summary>If rented out, the expected return date (due date of active rental).</summary>
        public DateTime? ExpectedReturnDate { get; set; }

        /// <summary>If rented out, the customer who has it.</summary>
        public string RentedByCustomer { get; set; }

        /// <summary>Number of customers in the reservation queue.</summary>
        public int ReservationQueueLength { get; set; }

        /// <summary>Whether the movie is overdue for return.</summary>
        public bool IsOverdue { get; set; }

        /// <summary>Days until available (0 = now, negative = overdue).</summary>
        public int DaysUntilAvailable { get; set; }

        /// <summary>
        /// Availability category for display grouping.
        /// </summary>
        public AvailabilityCategory Category
        {
            get
            {
                if (IsAvailable) return AvailabilityCategory.Available;
                if (IsOverdue) return AvailabilityCategory.Overdue;
                if (DaysUntilAvailable <= 2) return AvailabilityCategory.ComingSoon;
                return AvailabilityCategory.RentedOut;
            }
        }
    }

    /// <summary>
    /// Grouping category for availability display.
    /// </summary>
    public enum AvailabilityCategory
    {
        Available,
        ComingSoon,
        RentedOut,
        Overdue
    }

    /// <summary>
    /// A single day's availability snapshot for the calendar view.
    /// </summary>
    public class CalendarDay
    {
        public DateTime Date { get; set; }
        public DayOfWeek DayOfWeek => Date.DayOfWeek;
        public bool IsToday => Date.Date == DateTime.Today;
        public bool IsWeekend => DayOfWeek == DayOfWeek.Saturday || DayOfWeek == DayOfWeek.Sunday;

        /// <summary>Movies becoming available on this day (due back).</summary>
        public List<CalendarEvent> ReturningMovies { get; set; } = new List<CalendarEvent>();

        /// <summary>Reservations expiring on this day.</summary>
        public List<CalendarEvent> ExpiringReservations { get; set; } = new List<CalendarEvent>();

        /// <summary>Total events on this day.</summary>
        public int EventCount => ReturningMovies.Count + ExpiringReservations.Count;
    }

    /// <summary>
    /// An event on the availability calendar.
    /// </summary>
    public class CalendarEvent
    {
        public string MovieName { get; set; }
        public int MovieId { get; set; }
        public string Description { get; set; }
        public CalendarEventType EventType { get; set; }
    }

    /// <summary>
    /// Type of calendar event.
    /// </summary>
    public enum CalendarEventType
    {
        MovieReturning,
        ReservationExpiring
    }

    /// <summary>
    /// Summary statistics for the availability overview.
    /// </summary>
    public class AvailabilitySummary
    {
        public int TotalMovies { get; set; }
        public int AvailableNow { get; set; }
        public int RentedOut { get; set; }
        public int Overdue { get; set; }
        public int ComingSoonCount { get; set; }
        public double AvailabilityRate => TotalMovies > 0
            ? Math.Round(100.0 * AvailableNow / TotalMovies, 1) : 0;

        /// <summary>Genre with best availability (highest % available).</summary>
        public string BestAvailabilityGenre { get; set; }

        /// <summary>Genre with worst availability (lowest % available).</summary>
        public string WorstAvailabilityGenre { get; set; }
    }
}
