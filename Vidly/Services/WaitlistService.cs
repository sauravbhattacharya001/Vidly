using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Movie Waitlist Service — manages customer waitlists for out-of-stock movies.
    /// When all copies are rented out, customers can join a FIFO queue. When the movie
    /// is returned, the next person in line gets notified with a configurable pickup window.
    /// </summary>
    public class WaitlistService
    {
        private readonly List<WaitlistEntry> _entries = new List<WaitlistEntry>();
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IRentalRepository _rentalRepository;
        private int _nextId = 1;

        /// <summary>Default hours a notified customer has to pick up before the spot expires.</summary>
        public int DefaultPickupWindowHours { get; set; } = 48;

        /// <summary>Maximum waitlist entries a single customer can have at once.</summary>
        public int MaxEntriesPerCustomer { get; set; } = 10;

        public WaitlistService(
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository,
            IRentalRepository rentalRepository)
        {
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
        }

        /// <summary>
        /// Adds a customer to the waitlist for a movie.
        /// </summary>
        public WaitlistEntry JoinWaitlist(
            int customerId, int movieId,
            string note = null,
            NotificationMethod notification = NotificationMethod.Email,
            int? pickupWindowHours = null)
        {
            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new ArgumentException("Customer not found.", nameof(customerId));

            var movie = _movieRepository.GetById(movieId);
            if (movie == null)
                throw new ArgumentException("Movie not found.", nameof(movieId));

            // Check if already on waitlist
            if (_entries.Any(e => e.CustomerId == customerId &&
                                  e.MovieId == movieId &&
                                  e.Status == WaitlistStatus.Waiting))
                throw new InvalidOperationException(
                    $"{customer.Name} is already on the waitlist for \"{movie.Name}\".");

            // Check per-customer limit
            var activeCount = _entries.Count(e => e.CustomerId == customerId &&
                                                   (e.Status == WaitlistStatus.Waiting ||
                                                    e.Status == WaitlistStatus.Notified));
            if (activeCount >= MaxEntriesPerCustomer)
                throw new InvalidOperationException(
                    $"{customer.Name} has reached the maximum of {MaxEntriesPerCustomer} active waitlist entries.");

            var entry = new WaitlistEntry
            {
                Id = _nextId++,
                CustomerId = customerId,
                CustomerName = customer.Name,
                MovieId = movieId,
                MovieName = movie.Name,
                JoinedDate = DateTime.Now,
                Status = WaitlistStatus.Waiting,
                Note = note,
                PreferredNotification = notification,
                PickupWindowHours = pickupWindowHours ?? DefaultPickupWindowHours
            };

            _entries.Add(entry);
            return entry;
        }

        /// <summary>
        /// Removes a customer from the waitlist (cancellation).
        /// </summary>
        public WaitlistEntry LeaveWaitlist(int customerId, int movieId)
        {
            var entry = _entries.FirstOrDefault(e =>
                e.CustomerId == customerId &&
                e.MovieId == movieId &&
                (e.Status == WaitlistStatus.Waiting || e.Status == WaitlistStatus.Notified));

            if (entry == null)
                throw new InvalidOperationException("No active waitlist entry found.");

            entry.Status = WaitlistStatus.Cancelled;
            entry.CancelledDate = DateTime.Now;
            return entry;
        }

        /// <summary>
        /// Gets the customer's position in line for a movie (1-based).
        /// Returns 0 if not on the waitlist.
        /// </summary>
        public int GetPosition(int customerId, int movieId)
        {
            var waiting = _entries
                .Where(e => e.MovieId == movieId && e.Status == WaitlistStatus.Waiting)
                .OrderBy(e => e.JoinedDate)
                .ToList();

            for (int i = 0; i < waiting.Count; i++)
            {
                if (waiting[i].CustomerId == customerId)
                    return i + 1;
            }
            return 0;
        }

        /// <summary>
        /// Gets all waiting entries for a movie, ordered by join date (FIFO).
        /// </summary>
        public IReadOnlyList<WaitlistEntry> GetWaitlistForMovie(int movieId)
        {
            return _entries
                .Where(e => e.MovieId == movieId && e.Status == WaitlistStatus.Waiting)
                .OrderBy(e => e.JoinedDate)
                .ToList();
        }

        /// <summary>
        /// Gets all waitlist entries for a customer (all statuses).
        /// </summary>
        public IReadOnlyList<WaitlistEntry> GetCustomerWaitlist(int customerId)
        {
            return _entries
                .Where(e => e.CustomerId == customerId)
                .OrderByDescending(e => e.JoinedDate)
                .ToList();
        }

        /// <summary>
        /// Gets active waitlist entries for a customer (waiting or notified).
        /// </summary>
        public IReadOnlyList<WaitlistEntry> GetActiveCustomerWaitlist(int customerId)
        {
            return _entries
                .Where(e => e.CustomerId == customerId &&
                            (e.Status == WaitlistStatus.Waiting || e.Status == WaitlistStatus.Notified))
                .OrderBy(e => e.JoinedDate)
                .ToList();
        }

        /// <summary>
        /// Notifies the next person in line when a movie becomes available.
        /// Returns the notified entry, or null if no one is waiting.
        /// </summary>
        public WaitlistEntry NotifyNext(int movieId)
        {
            var next = _entries
                .Where(e => e.MovieId == movieId && e.Status == WaitlistStatus.Waiting)
                .OrderBy(e => e.JoinedDate)
                .FirstOrDefault();

            if (next == null)
                return null;

            next.Status = WaitlistStatus.Notified;
            next.NotifiedDate = DateTime.Now;
            return next;
        }

        /// <summary>
        /// Marks a notified entry as fulfilled (customer rented the movie).
        /// </summary>
        public WaitlistEntry MarkFulfilled(int customerId, int movieId)
        {
            var entry = _entries.FirstOrDefault(e =>
                e.CustomerId == customerId &&
                e.MovieId == movieId &&
                e.Status == WaitlistStatus.Notified);

            if (entry == null)
                throw new InvalidOperationException("No notified waitlist entry found for this customer and movie.");

            entry.Status = WaitlistStatus.Fulfilled;
            entry.FulfilledDate = DateTime.Now;
            return entry;
        }

        /// <summary>
        /// Expires notified entries whose pickup window has passed,
        /// and auto-notifies the next person in line.
        /// Returns all entries that were expired.
        /// </summary>
        public IReadOnlyList<WaitlistEntry> ProcessExpirations()
        {
            var expired = new List<WaitlistEntry>();

            var notified = _entries
                .Where(e => e.Status == WaitlistStatus.Notified)
                .ToList();

            foreach (var entry in notified)
            {
                if (entry.IsExpired)
                {
                    entry.Status = WaitlistStatus.Expired;
                    entry.CancelledDate = DateTime.Now;
                    expired.Add(entry);

                    // Auto-notify next in line
                    NotifyNext(entry.MovieId);
                }
            }

            return expired;
        }

        /// <summary>
        /// Estimates wait time in hours based on average rental duration for this movie.
        /// </summary>
        public double EstimateWaitHours(int customerId, int movieId)
        {
            var position = GetPosition(customerId, movieId);
            if (position == 0)
                return 0;

            // Get historical rental durations for this movie
            var rentals = _rentalRepository.GetByMovie(movieId)
                .Where(r => r.Status == RentalStatus.Returned && r.ReturnDate.HasValue)
                .ToList();

            double avgDurationHours;
            if (rentals.Count >= 3)
            {
                avgDurationHours = rentals
                    .Select(r => (r.ReturnDate.Value - r.RentalDate).TotalHours)
                    .Average();
            }
            else
            {
                // Default to 3 days if insufficient history
                avgDurationHours = 72;
            }

            return position * avgDurationHours;
        }

        /// <summary>
        /// Gets the total number of people waiting across all movies.
        /// </summary>
        public int GetTotalWaiting()
        {
            return _entries.Count(e => e.Status == WaitlistStatus.Waiting);
        }

        /// <summary>
        /// Generates a comprehensive waitlist analytics report.
        /// </summary>
        public WaitlistReport GetReport()
        {
            var all = _entries.ToList();
            var completed = all.Where(e =>
                e.Status == WaitlistStatus.Fulfilled ||
                e.Status == WaitlistStatus.Expired ||
                e.Status == WaitlistStatus.Cancelled).ToList();

            var fulfilled = all.Where(e => e.Status == WaitlistStatus.Fulfilled).ToList();
            var expired = all.Where(e => e.Status == WaitlistStatus.Expired).ToList();

            var fulfillmentRate = completed.Count > 0
                ? (double)fulfilled.Count / completed.Count
                : 0;

            var expirationRate = completed.Count > 0
                ? (double)expired.Count / completed.Count
                : 0;

            var avgWaitHours = completed.Count > 0
                ? completed.Average(e => e.WaitDuration.TotalHours)
                : 0;

            var mostWaitlisted = all
                .Where(e => e.Status == WaitlistStatus.Waiting)
                .GroupBy(e => new { e.MovieId, e.MovieName })
                .Select(g => new MovieWaitlistSummary
                {
                    MovieId = g.Key.MovieId,
                    MovieName = g.Key.MovieName,
                    WaitingCount = g.Count(),
                    AverageWaitHours = g.Average(e => e.WaitDuration.TotalHours)
                })
                .OrderByDescending(m => m.WaitingCount)
                .Take(10)
                .ToList();

            var mostActive = all
                .GroupBy(e => new { e.CustomerId, e.CustomerName })
                .Select(g => new CustomerWaitlistSummary
                {
                    CustomerId = g.Key.CustomerId,
                    CustomerName = g.Key.CustomerName,
                    TotalRequests = g.Count(),
                    FulfilledCount = g.Count(e => e.Status == WaitlistStatus.Fulfilled)
                })
                .OrderByDescending(c => c.TotalRequests)
                .Take(10)
                .ToList();

            var waiting = all.Count(e => e.Status == WaitlistStatus.Waiting);
            var notified = all.Count(e => e.Status == WaitlistStatus.Notified);
            var cancelled = all.Count(e => e.Status == WaitlistStatus.Cancelled);

            var summary = $"Waitlist Report\n" +
                          $"==============\n" +
                          $"Total entries: {all.Count}\n" +
                          $"  Waiting: {waiting}\n" +
                          $"  Notified: {notified}\n" +
                          $"  Fulfilled: {fulfilled.Count}\n" +
                          $"  Cancelled: {cancelled}\n" +
                          $"  Expired: {expired.Count}\n" +
                          $"Avg wait: {avgWaitHours:F1} hours\n" +
                          $"Fulfillment rate: {fulfillmentRate:P0}\n" +
                          $"Expiration rate: {expirationRate:P0}\n";

            if (mostWaitlisted.Count > 0)
            {
                summary += "\nMost Waitlisted Movies:\n";
                foreach (var m in mostWaitlisted.Take(5))
                    summary += $"  {m.MovieName}: {m.WaitingCount} waiting\n";
            }

            return new WaitlistReport
            {
                TotalEntries = all.Count,
                ActivelyWaiting = waiting,
                Notified = notified,
                Fulfilled = fulfilled.Count,
                Cancelled = cancelled,
                Expired = expired.Count,
                AverageWaitHours = avgWaitHours,
                FulfillmentRate = fulfillmentRate,
                ExpirationRate = expirationRate,
                MostWaitlistedMovies = mostWaitlisted,
                MostActiveCustomers = mostActive,
                TextSummary = summary
            };
        }

        /// <summary>
        /// Gets movies with the longest waitlists — candidates for additional copies.
        /// </summary>
        public IReadOnlyList<MovieWaitlistSummary> GetStockRecommendations(int minWaiting = 2)
        {
            return _entries
                .Where(e => e.Status == WaitlistStatus.Waiting)
                .GroupBy(e => new { e.MovieId, e.MovieName })
                .Where(g => g.Count() >= minWaiting)
                .Select(g => new MovieWaitlistSummary
                {
                    MovieId = g.Key.MovieId,
                    MovieName = g.Key.MovieName,
                    WaitingCount = g.Count(),
                    AverageWaitHours = g.Average(e => e.WaitDuration.TotalHours)
                })
                .OrderByDescending(m => m.WaitingCount)
                .ToList();
        }

        /// <summary>
        /// Bulk cancels all waiting entries for a movie (e.g., movie removed from catalog).
        /// </summary>
        public int CancelAllForMovie(int movieId)
        {
            var waiting = _entries
                .Where(e => e.MovieId == movieId &&
                            (e.Status == WaitlistStatus.Waiting || e.Status == WaitlistStatus.Notified))
                .ToList();

            foreach (var entry in waiting)
            {
                entry.Status = WaitlistStatus.Cancelled;
                entry.CancelledDate = DateTime.Now;
            }

            return waiting.Count;
        }

        /// <summary>
        /// Checks if a customer is on the waitlist for a specific movie.
        /// </summary>
        public bool IsOnWaitlist(int customerId, int movieId)
        {
            return _entries.Any(e =>
                e.CustomerId == customerId &&
                e.MovieId == movieId &&
                e.Status == WaitlistStatus.Waiting);
        }

        /// <summary>
        /// Gets a single entry by ID.
        /// </summary>
        public WaitlistEntry GetById(int entryId)
        {
            return _entries.FirstOrDefault(e => e.Id == entryId);
        }
    }
}
