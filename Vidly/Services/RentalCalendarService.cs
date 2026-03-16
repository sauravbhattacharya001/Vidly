using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Builds calendar data for rental events — checkouts, due dates, and returns.
    /// </summary>
    public class RentalCalendarService
    {
        private readonly IRentalRepository _rentalRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IClock _clock;

        public RentalCalendarService(
            IRentalRepository rentalRepository,
            ICustomerRepository customerRepository,
            IClock clock = null)
        {
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _clock = clock ?? new SystemClock();
        }

        /// <summary>
        /// Get calendar data for a given month/year, optionally filtered by customer.
        /// </summary>
        public CalendarMonth GetCalendarMonth(int year, int month, int? customerId = null)
        {
            if (month < 1 || month > 12)
                throw new ArgumentOutOfRangeException(nameof(month), "Month must be 1-12.");
            if (year < 2000 || year > 2100)
                throw new ArgumentOutOfRangeException(nameof(year), "Year must be 2000-2100.");

            var firstDay = new DateTime(year, month, 1);
            var lastDay = firstDay.AddMonths(1).AddDays(-1);
            var today = _clock.Now.Date;

            var allRentals = _rentalRepository.GetAll();
            if (customerId.HasValue)
                allRentals = allRentals.Where(r => r.CustomerId == customerId.Value);

            var rentalList = allRentals.ToList();

            // Build events for each rental
            var events = new List<CalendarEvent>();
            foreach (var rental in rentalList)
            {
                // Checkout event
                if (rental.RentalDate >= firstDay && rental.RentalDate <= lastDay)
                {
                    events.Add(new CalendarEvent
                    {
                        Date = rental.RentalDate.Date,
                        Type = CalendarEventType.Checkout,
                        RentalId = rental.Id,
                        MovieName = rental.MovieName ?? $"Movie #{rental.MovieId}",
                        CustomerName = rental.CustomerName ?? $"Customer #{rental.CustomerId}",
                        Label = $"📀 {rental.MovieName ?? $"Movie #{rental.MovieId}"}"
                    });
                }

                // Due date event
                if (rental.DueDate >= firstDay && rental.DueDate <= lastDay)
                {
                    var isOverdue = rental.Status != RentalStatus.Returned && today > rental.DueDate;
                    events.Add(new CalendarEvent
                    {
                        Date = rental.DueDate.Date,
                        Type = isOverdue ? CalendarEventType.Overdue : CalendarEventType.DueDate,
                        RentalId = rental.Id,
                        MovieName = rental.MovieName ?? $"Movie #{rental.MovieId}",
                        CustomerName = rental.CustomerName ?? $"Customer #{rental.CustomerId}",
                        Label = isOverdue
                            ? $"⚠️ {rental.MovieName ?? $"Movie #{rental.MovieId}"} (OVERDUE)"
                            : $"📅 {rental.MovieName ?? $"Movie #{rental.MovieId}"} due"
                    });
                }

                // Return event
                if (rental.ReturnDate.HasValue
                    && rental.ReturnDate.Value >= firstDay
                    && rental.ReturnDate.Value <= lastDay)
                {
                    events.Add(new CalendarEvent
                    {
                        Date = rental.ReturnDate.Value.Date,
                        Type = CalendarEventType.Return,
                        RentalId = rental.Id,
                        MovieName = rental.MovieName ?? $"Movie #{rental.MovieId}",
                        CustomerName = rental.CustomerName ?? $"Customer #{rental.CustomerId}",
                        Label = $"✅ {rental.MovieName ?? $"Movie #{rental.MovieId}"} returned"
                    });
                }
            }

            // Build day cells
            var startDow = (int)firstDay.DayOfWeek; // 0=Sun
            var daysInMonth = DateTime.DaysInMonth(year, month);
            var days = new List<CalendarDay>();

            for (int d = 1; d <= daysInMonth; d++)
            {
                var date = new DateTime(year, month, d);
                var dayEvents = events
                    .Where(e => e.Date == date)
                    .OrderBy(e => e.Type)
                    .ThenBy(e => e.MovieName)
                    .ToList();

                days.Add(new CalendarDay
                {
                    Date = date,
                    DayNumber = d,
                    IsToday = date == today,
                    IsWeekend = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday,
                    Events = dayEvents
                });
            }

            // Summary stats
            var totalCheckouts = events.Count(e => e.Type == CalendarEventType.Checkout);
            var totalDue = events.Count(e => e.Type == CalendarEventType.DueDate);
            var totalReturns = events.Count(e => e.Type == CalendarEventType.Return);
            var totalOverdue = events.Count(e => e.Type == CalendarEventType.Overdue);

            return new CalendarMonth
            {
                Year = year,
                Month = month,
                MonthName = firstDay.ToString("MMMM yyyy"),
                FirstDayOfWeek = startDow,
                Days = days,
                TotalCheckouts = totalCheckouts,
                TotalDueDates = totalDue,
                TotalReturns = totalReturns,
                TotalOverdue = totalOverdue,
                PreviousMonth = firstDay.AddMonths(-1),
                NextMonth = firstDay.AddMonths(1),
                CustomerId = customerId
            };
        }

        /// <summary>
        /// Get upcoming events in the next N days.
        /// </summary>
        public List<CalendarEvent> GetUpcomingEvents(int days = 7, int? customerId = null)
        {
            if (days < 1) days = 1;
            if (days > 90) days = 90;

            var today = _clock.Now.Date;
            var endDate = today.AddDays(days);

            var allRentals = _rentalRepository.GetAll();
            if (customerId.HasValue)
                allRentals = allRentals.Where(r => r.CustomerId == customerId.Value);

            var upcoming = new List<CalendarEvent>();
            foreach (var rental in allRentals)
            {
                if (rental.DueDate >= today && rental.DueDate <= endDate
                    && rental.Status != RentalStatus.Returned)
                {
                    upcoming.Add(new CalendarEvent
                    {
                        Date = rental.DueDate.Date,
                        Type = CalendarEventType.DueDate,
                        RentalId = rental.Id,
                        MovieName = rental.MovieName ?? $"Movie #{rental.MovieId}",
                        CustomerName = rental.CustomerName ?? $"Customer #{rental.CustomerId}",
                        Label = $"📅 {rental.MovieName} due"
                    });
                }
            }

            return upcoming.OrderBy(e => e.Date).ThenBy(e => e.MovieName).ToList();
        }
    }

    // ── Models ──

    public class CalendarMonth
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string MonthName { get; set; }
        public int FirstDayOfWeek { get; set; }
        public List<CalendarDay> Days { get; set; } = new List<CalendarDay>();
        public int TotalCheckouts { get; set; }
        public int TotalDueDates { get; set; }
        public int TotalReturns { get; set; }
        public int TotalOverdue { get; set; }
        public DateTime PreviousMonth { get; set; }
        public DateTime NextMonth { get; set; }
        public int? CustomerId { get; set; }
    }

    public class CalendarDay
    {
        public DateTime Date { get; set; }
        public int DayNumber { get; set; }
        public bool IsToday { get; set; }
        public bool IsWeekend { get; set; }
        public List<CalendarEvent> Events { get; set; } = new List<CalendarEvent>();
        public bool HasEvents => Events.Any();
    }

    public class CalendarEvent
    {
        public DateTime Date { get; set; }
        public CalendarEventType Type { get; set; }
        public int RentalId { get; set; }
        public string MovieName { get; set; }
        public string CustomerName { get; set; }
        public string Label { get; set; }
    }

    public enum CalendarEventType
    {
        Checkout = 1,
        DueDate = 2,
        Return = 3,
        Overdue = 4
    }
}
