using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Provides movie availability tracking, calendar views of upcoming returns,
    /// and availability search/filtering. Helps customers plan when to rent and
    /// staff manage inventory visibility.
    /// </summary>
    public class AvailabilityService
    {
        private readonly IMovieRepository _movieRepository;
        private readonly IRentalRepository _rentalRepository;
        private readonly IReservationRepository _reservationRepository;
        private readonly IClock _clock;

        public AvailabilityService(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository,
            IReservationRepository reservationRepository = null,
            IClock clock = null)
        {
            _clock = clock ?? new SystemClock();
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
            _reservationRepository = reservationRepository;
        }

        /// <summary>
        /// Gets availability status for all movies, optionally filtered.
        /// </summary>
        /// <param name="genre">Filter by genre.</param>
        /// <param name="availableOnly">Show only available movies.</param>
        /// <param name="query">Search movie name (case-insensitive).</param>
        /// <returns>List of movie availability records sorted by category then name.</returns>
        public List<MovieAvailability> GetAllAvailability(
            Genre? genre = null, bool availableOnly = false, string query = null)
        {
            var movies = _movieRepository.GetAll();
            var activeRentals = _rentalRepository.GetAll()
                .Where(r => r.Status != RentalStatus.Returned)
                .ToList();

            // Index active rentals by movieId for O(1) lookup
            var rentalByMovie = new Dictionary<int, Rental>();
            foreach (var r in activeRentals)
                rentalByMovie[r.MovieId] = r;

            // Index reservation counts by movieId
            var reservationCounts = new Dictionary<int, int>();
            if (_reservationRepository != null)
            {
                foreach (var movie in movies)
                {
                    var active = _reservationRepository.GetActiveByMovie(movie.Id);
                    if (active.Count > 0)
                        reservationCounts[movie.Id] = active.Count;
                }
            }

            var results = new List<MovieAvailability>();

            foreach (var movie in movies)
            {
                // Apply filters
                if (genre.HasValue && movie.Genre != genre.Value)
                    continue;
                if (!string.IsNullOrWhiteSpace(query) &&
                    movie.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                Rental activeRental;
                rentalByMovie.TryGetValue(movie.Id, out activeRental);
                var isAvailable = activeRental == null;

                if (availableOnly && !isAvailable)
                    continue;

                var isOverdue = activeRental != null && activeRental.IsOverdue;
                var daysUntil = 0;
                if (activeRental != null)
                {
                    daysUntil = (int)Math.Ceiling(
                        (activeRental.DueDate - _clock.Today).TotalDays);
                }

                int queueLen;
                reservationCounts.TryGetValue(movie.Id, out queueLen);

                results.Add(new MovieAvailability
                {
                    MovieId = movie.Id,
                    MovieName = movie.Name,
                    Genre = movie.Genre,
                    Rating = movie.Rating,
                    IsAvailable = isAvailable,
                    ExpectedReturnDate = activeRental?.DueDate,
                    RentedByCustomer = activeRental?.CustomerName,
                    IsOverdue = isOverdue,
                    DaysUntilAvailable = isAvailable ? 0 : daysUntil,
                    ReservationQueueLength = queueLen
                });
            }

            // Sort: Available first, then ComingSoon, RentedOut, Overdue; then by name
            return results
                .OrderBy(a => (int)a.Category)
                .ThenBy(a => a.MovieName)
                .ToList();
        }

        /// <summary>
        /// Gets availability for a single movie with detailed info.
        /// Directly looks up the movie and its active rental instead of
        /// computing availability for the entire catalog — O(1) vs O(M).
        /// </summary>
        public MovieAvailability GetMovieAvailability(int movieId)
        {
            var movie = _movieRepository.GetById(movieId);
            if (movie == null) return null;

            var activeRental = _rentalRepository.GetAll()
                .FirstOrDefault(r => r.MovieId == movieId && r.Status != RentalStatus.Returned);

            var isAvailable = activeRental == null;
            var isOverdue = activeRental != null && activeRental.IsOverdue;
            var daysUntil = 0;
            if (activeRental != null)
            {
                daysUntil = (int)Math.Ceiling(
                    (activeRental.DueDate - _clock.Today).TotalDays);
            }

            int queueLen = 0;
            if (_reservationRepository != null)
                queueLen = _reservationRepository.GetActiveByMovie(movieId).Count;

            return new MovieAvailability
            {
                MovieId = movie.Id,
                MovieName = movie.Name,
                Genre = movie.Genre,
                Rating = movie.Rating,
                IsAvailable = isAvailable,
                ExpectedReturnDate = activeRental?.DueDate,
                RentedByCustomer = activeRental?.CustomerName,
                IsOverdue = isOverdue,
                DaysUntilAvailable = isAvailable ? 0 : daysUntil,
                ReservationQueueLength = queueLen
            };
        }

        /// <summary>
        /// Generates a calendar of upcoming movie returns and reservation
        /// expirations for the next N days.
        /// </summary>
        /// <param name="days">Number of days to include (default 14).</param>
        /// <returns>List of calendar days with events.</returns>
        public List<CalendarDay> GetAvailabilityCalendar(int days = 14)
        {
            if (days < 1) days = 1;
            if (days > 90) days = 90;

            var startDate = _clock.Today;
            var endDate = startDate.AddDays(days);

            // Get all active rentals with due dates in range
            var activeRentals = _rentalRepository.GetAll()
                .Where(r => r.Status != RentalStatus.Returned)
                .ToList();

            // Index by due date
            var rentalsByDueDate = new Dictionary<DateTime, List<Rental>>();
            foreach (var r in activeRentals)
            {
                var dueDay = r.DueDate.Date;
                if (dueDay < startDate || dueDay >= endDate) continue;

                List<Rental> list;
                if (!rentalsByDueDate.TryGetValue(dueDay, out list))
                {
                    list = new List<Rental>();
                    rentalsByDueDate[dueDay] = list;
                }
                list.Add(r);
            }

            // Build calendar
            var calendar = new List<CalendarDay>();
            for (var d = startDate; d < endDate; d = d.AddDays(1))
            {
                var day = new CalendarDay { Date = d };

                List<Rental> dueRentals;
                if (rentalsByDueDate.TryGetValue(d, out dueRentals))
                {
                    foreach (var r in dueRentals)
                    {
                        day.ReturningMovies.Add(new CalendarEvent
                        {
                            MovieId = r.MovieId,
                            MovieName = r.MovieName,
                            Description = $"Due back from {r.CustomerName}",
                            EventType = CalendarEventType.MovieReturning
                        });
                    }
                }

                calendar.Add(day);
            }

            return calendar;
        }

        /// <summary>
        /// Gets a summary of overall movie availability.
        /// </summary>
        public AvailabilitySummary GetSummary()
        {
            var all = GetAllAvailability();

            // Single pass to count categories instead of 4 separate .Count() enumerations
            int available = 0, rentedOut = 0, overdue = 0, comingSoon = 0;
            foreach (var a in all)
            {
                if (a.IsAvailable) available++;
                else if (a.IsOverdue) overdue++;
                else rentedOut++;

                if (a.Category == AvailabilityCategory.ComingSoon) comingSoon++;
            }

            var summary = new AvailabilitySummary
            {
                TotalMovies = all.Count,
                AvailableNow = available,
                RentedOut = rentedOut,
                Overdue = overdue,
                ComingSoonCount = comingSoon
            };

            // Find best/worst genre availability
            var genreGroups = all
                .Where(a => a.Genre.HasValue)
                .GroupBy(a => a.Genre.Value)
                .Select(g => new
                {
                    Genre = g.Key,
                    Total = g.Count(),
                    Available = g.Count(a => a.IsAvailable),
                    Rate = g.Count() > 0
                        ? 100.0 * g.Count(a => a.IsAvailable) / g.Count()
                        : 0
                })
                .Where(g => g.Total >= 2) // need at least 2 movies for meaningful stats
                .ToList();

            if (genreGroups.Count > 0)
            {
                summary.BestAvailabilityGenre = genreGroups
                    .OrderByDescending(g => g.Rate).First().Genre.ToString();
                summary.WorstAvailabilityGenre = genreGroups
                    .OrderBy(g => g.Rate).First().Genre.ToString();
            }

            return summary;
        }

        /// <summary>
        /// Finds the next available date for a specific movie.
        /// Returns today if available now, or the due date of the current rental.
        /// </summary>
        public DateTime GetNextAvailableDate(int movieId)
        {
            var rental = _rentalRepository.GetAll()
                .FirstOrDefault(r => r.MovieId == movieId && r.Status != RentalStatus.Returned);

            return rental == null ? _clock.Today : rental.DueDate;
        }

        /// <summary>
        /// Gets movies becoming available within the next N days.
        /// </summary>
        public List<MovieAvailability> GetComingSoon(int withinDays = 3)
        {
            return GetAllAvailability()
                .Where(a => !a.IsAvailable && a.DaysUntilAvailable > 0
                    && a.DaysUntilAvailable <= withinDays)
                .OrderBy(a => a.DaysUntilAvailable)
                .ToList();
        }
    }
}
