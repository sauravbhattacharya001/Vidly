using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Manages movie reservations (holds). Customers can reserve movies
    /// that are currently rented out. When the movie is returned, the
    /// next customer in the queue gets a pickup window. If they don't
    /// pick up in time, the reservation expires and the next person
    /// in line is notified.
    /// </summary>
    public class ReservationService
    {
        private readonly IReservationRepository _reservationRepo;
        private readonly IRentalRepository _rentalRepo;
        private readonly IMovieRepository _movieRepo;
        private readonly ICustomerRepository _customerRepo;

        /// <summary>Days a customer has to pick up once notified.</summary>
        public const int PickupWindowDays = 2;

        /// <summary>Maximum active reservations per customer.</summary>
        public const int MaxReservationsPerCustomer = 5;

        /// <summary>Maximum queue depth per movie.</summary>
        public const int MaxQueueDepth = 10;

        public ReservationService(
            IReservationRepository reservationRepo,
            IRentalRepository rentalRepo,
            IMovieRepository movieRepo,
            ICustomerRepository customerRepo)
        {
            _reservationRepo = reservationRepo
                ?? throw new ArgumentNullException(nameof(reservationRepo));
            _rentalRepo = rentalRepo
                ?? throw new ArgumentNullException(nameof(rentalRepo));
            _movieRepo = movieRepo
                ?? throw new ArgumentNullException(nameof(movieRepo));
            _customerRepo = customerRepo
                ?? throw new ArgumentNullException(nameof(customerRepo));
        }

        // ── Reserve ──────────────────────────────────────────────────

        /// <summary>
        /// Place a reservation on a movie. The movie must be currently
        /// rented out (otherwise the customer should just rent it).
        /// </summary>
        /// <returns>The created reservation with queue position.</returns>
        /// <exception cref="ArgumentException">
        /// Invalid customer or movie ID.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Movie is available, customer already has a reservation, queue
        /// is full, or customer has too many active reservations.
        /// </exception>
        public Reservation PlaceReservation(int customerId, int movieId)
        {
            var customer = _customerRepo.GetById(customerId);
            if (customer == null)
                throw new ArgumentException(
                    $"Customer {customerId} not found.", nameof(customerId));

            var movie = _movieRepo.GetById(movieId);
            if (movie == null)
                throw new ArgumentException(
                    $"Movie {movieId} not found.", nameof(movieId));

            // Can only reserve movies that are currently rented out
            if (!_rentalRepo.IsMovieRentedOut(movieId))
                throw new InvalidOperationException(
                    $"\"{movie.Name}\" is currently available — rent it instead of reserving.");

            // Can't double-reserve
            if (_reservationRepo.HasActiveReservation(customerId, movieId))
                throw new InvalidOperationException(
                    $"You already have an active reservation for \"{movie.Name}\".");

            // Per-customer limit
            var customerReservations = _reservationRepo.GetByCustomer(customerId)
                .Count(r => r.Status == ReservationStatus.Waiting ||
                            r.Status == ReservationStatus.Ready);
            if (customerReservations >= MaxReservationsPerCustomer)
                throw new InvalidOperationException(
                    $"Maximum of {MaxReservationsPerCustomer} active reservations reached.");

            // Per-movie queue limit
            var movieQueue = _reservationRepo.GetActiveByMovie(movieId);
            if (movieQueue.Count >= MaxQueueDepth)
                throw new InvalidOperationException(
                    $"The reservation queue for \"{movie.Name}\" is full ({MaxQueueDepth} max).");

            var position = movieQueue.Count + 1;

            var reservation = new Reservation
            {
                CustomerId = customerId,
                CustomerName = customer.Name,
                MovieId = movieId,
                MovieName = movie.Name,
                ReservedDate = DateTime.Today,
                QueuePosition = position,
                Status = ReservationStatus.Waiting
            };

            _reservationRepo.Add(reservation);
            return reservation;
        }

        // ── Cancel ───────────────────────────────────────────────────

        /// <summary>
        /// Cancel an active reservation. If the cancelled reservation was
        /// first in line, the queue is compacted and the next person moves up.
        /// </summary>
        /// <returns>The cancelled reservation.</returns>
        /// <exception cref="InvalidOperationException">
        /// Reservation not found or already in a terminal state.
        /// </exception>
        public Reservation CancelReservation(int reservationId)
        {
            var reservation = _reservationRepo.GetById(reservationId);
            if (reservation == null)
                throw new InvalidOperationException(
                    $"Reservation {reservationId} not found.");

            if (reservation.Status == ReservationStatus.Fulfilled ||
                reservation.Status == ReservationStatus.Cancelled ||
                reservation.Status == ReservationStatus.Expired)
                throw new InvalidOperationException(
                    $"Reservation {reservationId} is already {reservation.Status}.");

            reservation.Status = ReservationStatus.Cancelled;
            _reservationRepo.Update(reservation);

            // Compact queue positions
            CompactQueue(reservation.MovieId);

            return reservation;
        }

        // ── Movie Returned ───────────────────────────────────────────

        /// <summary>
        /// Called when a movie is returned. Activates the next reservation
        /// in the queue by setting its status to Ready with a pickup window.
        /// </summary>
        /// <returns>
        /// The reservation that was activated, or null if no one is waiting.
        /// </returns>
        public Reservation NotifyNextInQueue(int movieId)
        {
            // First, expire any ready reservations that have timed out
            ExpireOverdueReservations(movieId);

            // Compact queue after expiring reservations so remaining
            // positions stay sequential (1, 2, 3…) with no gaps.
            CompactQueue(movieId);

            var next = _reservationRepo.GetNextInQueue(movieId);
            if (next == null)
                return null;

            next.Status = ReservationStatus.Ready;
            next.ExpiresDate = DateTime.Today.AddDays(PickupWindowDays);
            _reservationRepo.Update(next);

            return next;
        }

        // ── Fulfill ──────────────────────────────────────────────────

        /// <summary>
        /// Mark a Ready reservation as fulfilled (customer picked up the movie).
        /// </summary>
        /// <returns>The fulfilled reservation.</returns>
        /// <exception cref="InvalidOperationException">
        /// Reservation not found, not in Ready state, or pickup window expired.
        /// </exception>
        public Reservation FulfillReservation(int reservationId)
        {
            var reservation = _reservationRepo.GetById(reservationId);
            if (reservation == null)
                throw new InvalidOperationException(
                    $"Reservation {reservationId} not found.");

            if (reservation.Status != ReservationStatus.Ready)
                throw new InvalidOperationException(
                    $"Reservation {reservationId} is not ready for pickup (status: {reservation.Status}).");

            if (reservation.IsExpired)
            {
                // Auto-expire and move to next
                reservation.Status = ReservationStatus.Expired;
                _reservationRepo.Update(reservation);
                CompactQueue(reservation.MovieId);
                throw new InvalidOperationException(
                    "Pickup window has expired. The reservation has been cancelled.");
            }

            reservation.Status = ReservationStatus.Fulfilled;
            reservation.FulfilledDate = DateTime.Today;
            _reservationRepo.Update(reservation);

            // Compact remaining queue
            CompactQueue(reservation.MovieId);

            return reservation;
        }

        // ── Queries ──────────────────────────────────────────────────

        /// <summary>
        /// Get all reservations for a customer.
        /// </summary>
        public IReadOnlyList<Reservation> GetCustomerReservations(int customerId)
        {
            return _reservationRepo.GetByCustomer(customerId);
        }

        /// <summary>
        /// Get the reservation queue for a movie.
        /// </summary>
        public IReadOnlyList<Reservation> GetMovieQueue(int movieId)
        {
            return _reservationRepo.GetActiveByMovie(movieId);
        }

        /// <summary>
        /// Get queue position for a specific customer + movie, or 0 if none.
        /// </summary>
        public int GetQueuePosition(int customerId, int movieId)
        {
            var queue = _reservationRepo.GetActiveByMovie(movieId);
            var reservation = queue.FirstOrDefault(r => r.CustomerId == customerId);
            return reservation?.QueuePosition ?? 0;
        }

        /// <summary>
        /// Estimated wait time in days based on queue position and average
        /// rental duration. Returns -1 if no reservation exists.
        /// </summary>
        public int EstimateWaitDays(int customerId, int movieId)
        {
            var position = GetQueuePosition(customerId, movieId);
            if (position == 0)
                return -1;

            // Estimate based on average rental length from history
            var movieRentals = _rentalRepo.GetByMovie(movieId);
            var completedRentals = movieRentals
                .Where(r => r.Status == RentalStatus.Returned && r.ReturnDate.HasValue)
                .ToList();

            double avgDays;
            if (completedRentals.Count > 0)
            {
                avgDays = completedRentals
                    .Average(r => (r.ReturnDate.Value - r.RentalDate).TotalDays);
            }
            else
            {
                // Default to 7 days if no history
                avgDays = 7.0;
            }

            return (int)Math.Ceiling(avgDays * position);
        }

        /// <summary>
        /// Check if a movie has any active reservations.
        /// </summary>
        public bool HasReservations(int movieId)
        {
            return _reservationRepo.GetActiveByMovie(movieId).Count > 0;
        }

        /// <summary>
        /// Search reservations by customer name or movie name,
        /// optionally filtered by status.
        /// </summary>
        public IReadOnlyList<Reservation> Search(
            string query, ReservationStatus? status = null)
        {
            return _reservationRepo.Search(query, status);
        }

        /// <summary>
        /// Get aggregate statistics about reservations.
        /// </summary>
        public ReservationStats GetStats()
        {
            var all = _reservationRepo.GetAll();

            var waiting = all.Where(r => r.Status == ReservationStatus.Waiting).ToList();
            var ready = all.Where(r => r.Status == ReservationStatus.Ready).ToList();
            var fulfilled = all.Where(r => r.Status == ReservationStatus.Fulfilled).ToList();
            var cancelled = all.Count(r => r.Status == ReservationStatus.Cancelled);
            var expired = all.Count(r => r.Status == ReservationStatus.Expired);

            return new ReservationStats
            {
                TotalReservations = all.Count,
                WaitingCount = waiting.Count,
                ReadyCount = ready.Count,
                FulfilledCount = fulfilled.Count,
                CancelledCount = cancelled,
                ExpiredCount = expired,
                AverageWaitDays = fulfilled.Count > 0
                    ? Math.Round(fulfilled.Average(r => r.DaysWaiting), 1)
                    : 0,
                FulfillmentRate = (fulfilled.Count + cancelled + expired) > 0
                    ? Math.Round(
                        (double)fulfilled.Count /
                        (fulfilled.Count + cancelled + expired) * 100, 1)
                    : 0,
                MostReservedMovies = all
                    .GroupBy(r => new { r.MovieId, r.MovieName })
                    .Select(g => new MovieReservationCount
                    {
                        MovieId = g.Key.MovieId,
                        MovieName = g.Key.MovieName,
                        TotalReservations = g.Count(),
                        ActiveReservations = g.Count(r =>
                            r.Status == ReservationStatus.Waiting ||
                            r.Status == ReservationStatus.Ready)
                    })
                    .OrderByDescending(m => m.TotalReservations)
                    .Take(5)
                    .ToList()
            };
        }

        /// <summary>
        /// Generate a formatted summary string of the queue for a movie.
        /// </summary>
        public string GetQueueSummary(int movieId)
        {
            var movie = _movieRepo.GetById(movieId);
            if (movie == null) return "Movie not found.";

            var queue = _reservationRepo.GetActiveByMovie(movieId);
            if (queue.Count == 0)
                return $"No active reservations for \"{movie.Name}\".";

            var lines = new List<string>
            {
                $"Reservation queue for \"{movie.Name}\" ({queue.Count} waiting):"
            };

            foreach (var r in queue)
            {
                var status = r.Status == ReservationStatus.Ready
                    ? $"READY — pickup by {r.ExpiresDate?.ToString("MMM d", CultureInfo.InvariantCulture) ?? "N/A"}"
                    : $"Waiting (since {r.ReservedDate.ToString("MMM d", CultureInfo.InvariantCulture)})";
                lines.Add($"  #{r.QueuePosition}: {r.CustomerName} — {status}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        // ── Expiration ───────────────────────────────────────────────

        /// <summary>
        /// Process all expired reservations across all movies.
        /// Returns the count of reservations that were expired.
        /// </summary>
        public int ProcessExpiredReservations()
        {
            var expired = _reservationRepo.GetExpired();
            foreach (var reservation in expired)
            {
                reservation.Status = ReservationStatus.Expired;
                _reservationRepo.Update(reservation);
                CompactQueue(reservation.MovieId);
            }
            return expired.Count;
        }

        // ── Private Helpers ──────────────────────────────────────────

        /// <summary>
        /// Expire any Ready reservations for a movie whose pickup window
        /// has passed.
        /// </summary>
        private void ExpireOverdueReservations(int movieId)
        {
            var active = _reservationRepo.GetActiveByMovie(movieId);
            foreach (var r in active)
            {
                if (r.Status == ReservationStatus.Ready && r.IsExpired)
                {
                    r.Status = ReservationStatus.Expired;
                    _reservationRepo.Update(r);
                }
            }
        }

        /// <summary>
        /// Recalculate queue positions after a cancellation/expiration,
        /// keeping positions sequential with no gaps.
        /// </summary>
        private void CompactQueue(int movieId)
        {
            var active = _reservationRepo.GetActiveByMovie(movieId);
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].QueuePosition != i + 1)
                {
                    var r = active[i];
                    r.QueuePosition = i + 1;
                    _reservationRepo.Update(r);
                }
            }
        }
    }

    // ── Stats Models ─────────────────────────────────────────────────

    /// <summary>Aggregate reservation statistics.</summary>
    public class ReservationStats
    {
        public int TotalReservations { get; set; }
        public int WaitingCount { get; set; }
        public int ReadyCount { get; set; }
        public int FulfilledCount { get; set; }
        public int CancelledCount { get; set; }
        public int ExpiredCount { get; set; }

        /// <summary>Average days between reservation and fulfillment.</summary>
        public double AverageWaitDays { get; set; }

        /// <summary>
        /// Percentage of completed reservations that were fulfilled
        /// (vs cancelled or expired).
        /// </summary>
        public double FulfillmentRate { get; set; }

        /// <summary>Top 5 most reserved movies.</summary>
        public List<MovieReservationCount> MostReservedMovies { get; set; }
            = new List<MovieReservationCount>();
    }

    /// <summary>Reservation count for a specific movie.</summary>
    public class MovieReservationCount
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public int TotalReservations { get; set; }
        public int ActiveReservations { get; set; }
    }
}
