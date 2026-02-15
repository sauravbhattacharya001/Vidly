using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Thread-safe in-memory rental repository with business logic.
    /// Late fee: $1.50 per day overdue.
    /// Default rental period: 7 days.
    /// Default daily rate: $3.99.
    /// </summary>
    public class InMemoryRentalRepository : IRentalRepository
    {
        /// <summary>Late fee per overdue day.</summary>
        public const decimal LateFeePerDay = 1.50m;

        /// <summary>Default rental duration in days.</summary>
        public const int DefaultRentalDays = 7;

        /// <summary>Default daily rental rate.</summary>
        public const decimal DefaultDailyRate = 3.99m;

        private static readonly List<Rental> _rentals = new List<Rental>
        {
            new Rental
            {
                Id = 1,
                CustomerId = 1,
                CustomerName = "John Smith",
                MovieId = 1,
                MovieName = "Shrek!",
                RentalDate = DateTime.Today.AddDays(-3),
                DueDate = DateTime.Today.AddDays(4),
                DailyRate = 3.99m,
                Status = RentalStatus.Active
            },
            new Rental
            {
                Id = 2,
                CustomerId = 2,
                CustomerName = "Jane Doe",
                MovieId = 2,
                MovieName = "The Godfather",
                RentalDate = DateTime.Today.AddDays(-10),
                DueDate = DateTime.Today.AddDays(-3),
                DailyRate = 3.99m,
                Status = RentalStatus.Active
            },
            new Rental
            {
                Id = 3,
                CustomerId = 4,
                CustomerName = "Alice Johnson",
                MovieId = 3,
                MovieName = "Toy Story",
                RentalDate = DateTime.Today.AddDays(-14),
                DueDate = DateTime.Today.AddDays(-7),
                ReturnDate = DateTime.Today.AddDays(-6),
                DailyRate = 3.99m,
                LateFee = 0m,
                Status = RentalStatus.Returned
            }
        };

        private static readonly object _lock = new object();

        public Rental GetById(int id)
        {
            lock (_lock)
            {
                var rental = _rentals.SingleOrDefault(r => r.Id == id);
                if (rental == null) return null;
                RefreshStatus(rental);
                return Clone(rental);
            }
        }

        public IReadOnlyList<Rental> GetAll()
        {
            lock (_lock)
            {
                foreach (var r in _rentals) RefreshStatus(r);
                return _rentals.Select(Clone).ToList().AsReadOnly();
            }
        }

        public void Add(Rental rental)
        {
            if (rental == null)
                throw new ArgumentNullException(nameof(rental));

            lock (_lock)
            {
                rental.Id = _rentals.Any() ? _rentals.Max(r => r.Id) + 1 : 1;

                if (rental.DailyRate <= 0)
                    rental.DailyRate = DefaultDailyRate;

                if (rental.RentalDate == default)
                    rental.RentalDate = DateTime.Today;

                if (rental.DueDate == default)
                    rental.DueDate = rental.RentalDate.AddDays(DefaultRentalDays);

                rental.Status = RentalStatus.Active;
                rental.ReturnDate = null;
                rental.LateFee = 0;

                _rentals.Add(rental);
            }
        }

        public void Update(Rental rental)
        {
            if (rental == null)
                throw new ArgumentNullException(nameof(rental));

            lock (_lock)
            {
                var existing = _rentals.SingleOrDefault(r => r.Id == rental.Id);
                if (existing == null)
                    throw new KeyNotFoundException(
                        $"Rental with Id {rental.Id} not found.");

                existing.CustomerId = rental.CustomerId;
                existing.CustomerName = rental.CustomerName;
                existing.MovieId = rental.MovieId;
                existing.MovieName = rental.MovieName;
                existing.RentalDate = rental.RentalDate;
                existing.DueDate = rental.DueDate;
                existing.ReturnDate = rental.ReturnDate;
                existing.DailyRate = rental.DailyRate;
                existing.LateFee = rental.LateFee;
                existing.Status = rental.Status;
            }
        }

        public void Remove(int id)
        {
            lock (_lock)
            {
                var rental = _rentals.SingleOrDefault(r => r.Id == id);
                if (rental == null)
                    throw new KeyNotFoundException(
                        $"Rental with Id {id} not found.");

                _rentals.Remove(rental);
            }
        }

        public IReadOnlyList<Rental> GetActiveByCustomer(int customerId)
        {
            lock (_lock)
            {
                foreach (var r in _rentals) RefreshStatus(r);
                return _rentals
                    .Where(r => r.CustomerId == customerId && r.Status != RentalStatus.Returned)
                    .OrderBy(r => r.DueDate)
                    .Select(Clone)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public IReadOnlyList<Rental> GetByMovie(int movieId)
        {
            lock (_lock)
            {
                foreach (var r in _rentals) RefreshStatus(r);
                return _rentals
                    .Where(r => r.MovieId == movieId)
                    .OrderByDescending(r => r.RentalDate)
                    .Select(Clone)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public IReadOnlyList<Rental> GetOverdue()
        {
            lock (_lock)
            {
                foreach (var r in _rentals) RefreshStatus(r);
                return _rentals
                    .Where(r => r.Status == RentalStatus.Overdue)
                    .OrderBy(r => r.DueDate)
                    .Select(Clone)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public IReadOnlyList<Rental> Search(string query, RentalStatus? status)
        {
            lock (_lock)
            {
                foreach (var r in _rentals) RefreshStatus(r);
                IEnumerable<Rental> results = _rentals;

                if (!string.IsNullOrWhiteSpace(query))
                {
                    results = results.Where(r =>
                        (r.CustomerName != null &&
                         r.CustomerName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (r.MovieName != null &&
                         r.MovieName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
                }

                if (status.HasValue)
                {
                    results = results.Where(r => r.Status == status.Value);
                }

                return results
                    .OrderByDescending(r => r.RentalDate)
                    .Select(Clone)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public Rental ReturnRental(int rentalId)
        {
            lock (_lock)
            {
                var rental = _rentals.SingleOrDefault(r => r.Id == rentalId);
                if (rental == null)
                    throw new KeyNotFoundException(
                        $"Rental with Id {rentalId} not found.");

                if (rental.Status == RentalStatus.Returned)
                    throw new InvalidOperationException(
                        $"Rental {rentalId} has already been returned.");

                rental.ReturnDate = DateTime.Today;
                rental.Status = RentalStatus.Returned;

                // Calculate late fee
                if (rental.ReturnDate.Value > rental.DueDate)
                {
                    var overdueDays = (int)(rental.ReturnDate.Value - rental.DueDate).TotalDays;
                    rental.LateFee = overdueDays * LateFeePerDay;
                }
                else
                {
                    rental.LateFee = 0;
                }

                return Clone(rental);
            }
        }

        public bool IsMovieRentedOut(int movieId)
        {
            lock (_lock)
            {
                return _rentals.Any(r =>
                    r.MovieId == movieId && r.Status != RentalStatus.Returned);
            }
        }

        public RentalStats GetStats()
        {
            lock (_lock)
            {
                foreach (var r in _rentals) RefreshStatus(r);
                return new RentalStats
                {
                    TotalRentals = _rentals.Count,
                    ActiveRentals = _rentals.Count(r => r.Status == RentalStatus.Active),
                    OverdueRentals = _rentals.Count(r => r.Status == RentalStatus.Overdue),
                    ReturnedRentals = _rentals.Count(r => r.Status == RentalStatus.Returned),
                    TotalRevenue = _rentals.Sum(r => r.TotalCost),
                    TotalLateFees = _rentals.Sum(r => r.LateFee)
                };
            }
        }

        /// <summary>
        /// Auto-update status based on dates (Active → Overdue if past due).
        /// </summary>
        private static void RefreshStatus(Rental rental)
        {
            if (rental.Status != RentalStatus.Returned && DateTime.Today > rental.DueDate)
            {
                rental.Status = RentalStatus.Overdue;
            }
        }

        /// <summary>
        /// Creates a defensive copy.
        /// </summary>
        private static Rental Clone(Rental source)
        {
            return new Rental
            {
                Id = source.Id,
                CustomerId = source.CustomerId,
                CustomerName = source.CustomerName,
                MovieId = source.MovieId,
                MovieName = source.MovieName,
                RentalDate = source.RentalDate,
                DueDate = source.DueDate,
                ReturnDate = source.ReturnDate,
                DailyRate = source.DailyRate,
                LateFee = source.LateFee,
                Status = source.Status
            };
        }
    }
}
