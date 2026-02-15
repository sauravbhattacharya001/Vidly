using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Thread-safe in-memory rental repository with business logic.
    /// Uses Dictionary for O(1) lookups by ID, counter-based ID generation,
    /// single-pass statistics, and a HashSet for O(1) rented-movie checks.
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

        private static readonly Dictionary<int, Rental> _rentals;

        /// <summary>
        /// Tracks movie IDs that are currently rented out (not returned).
        /// Provides O(1) availability checks instead of scanning the full list.
        /// </summary>
        private static readonly HashSet<int> _rentedMovieIds;

        private static readonly object _lock = new object();
        private static int _nextId;

        static InMemoryRentalRepository()
        {
            var seedData = new[]
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

            _rentals = new Dictionary<int, Rental>();
            _rentedMovieIds = new HashSet<int>();

            foreach (var r in seedData)
            {
                _rentals[r.Id] = r;
                if (r.Status != RentalStatus.Returned)
                    _rentedMovieIds.Add(r.MovieId);
            }

            _nextId = 4;
        }

        public Rental GetById(int id)
        {
            lock (_lock)
            {
                if (!_rentals.TryGetValue(id, out var rental))
                    return null;

                RefreshStatus(rental);
                return Clone(rental);
            }
        }

        public IReadOnlyList<Rental> GetAll()
        {
            lock (_lock)
            {
                var result = new List<Rental>(_rentals.Count);
                foreach (var r in _rentals.Values)
                {
                    RefreshStatus(r);
                    result.Add(Clone(r));
                }
                return result.AsReadOnly();
            }
        }

        public void Add(Rental rental)
        {
            if (rental == null)
                throw new ArgumentNullException(nameof(rental));

            lock (_lock)
            {
                rental.Id = _nextId++;

                if (rental.DailyRate <= 0)
                    rental.DailyRate = DefaultDailyRate;

                if (rental.RentalDate == default)
                    rental.RentalDate = DateTime.Today;

                if (rental.DueDate == default)
                    rental.DueDate = rental.RentalDate.AddDays(DefaultRentalDays);

                rental.Status = RentalStatus.Active;
                rental.ReturnDate = null;
                rental.LateFee = 0;

                _rentals[rental.Id] = rental;
                _rentedMovieIds.Add(rental.MovieId);
            }
        }

        public void Update(Rental rental)
        {
            if (rental == null)
                throw new ArgumentNullException(nameof(rental));

            lock (_lock)
            {
                if (!_rentals.TryGetValue(rental.Id, out var existing))
                    throw new KeyNotFoundException(
                        $"Rental with Id {rental.Id} not found.");

                // If the movie changed or status changed, update the rented set
                if (existing.Status != RentalStatus.Returned)
                    _rentedMovieIds.Remove(existing.MovieId);

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

                if (existing.Status != RentalStatus.Returned)
                    _rentedMovieIds.Add(existing.MovieId);
            }
        }

        public void Remove(int id)
        {
            lock (_lock)
            {
                if (!_rentals.TryGetValue(id, out var rental))
                    throw new KeyNotFoundException(
                        $"Rental with Id {id} not found.");

                if (rental.Status != RentalStatus.Returned)
                    _rentedMovieIds.Remove(rental.MovieId);

                _rentals.Remove(id);
            }
        }

        public IReadOnlyList<Rental> GetActiveByCustomer(int customerId)
        {
            lock (_lock)
            {
                var result = new List<Rental>();
                foreach (var r in _rentals.Values)
                {
                    RefreshStatus(r);
                    if (r.CustomerId == customerId && r.Status != RentalStatus.Returned)
                        result.Add(Clone(r));
                }
                result.Sort((a, b) => a.DueDate.CompareTo(b.DueDate));
                return result.AsReadOnly();
            }
        }

        public IReadOnlyList<Rental> GetByMovie(int movieId)
        {
            lock (_lock)
            {
                var result = new List<Rental>();
                foreach (var r in _rentals.Values)
                {
                    RefreshStatus(r);
                    if (r.MovieId == movieId)
                        result.Add(Clone(r));
                }
                result.Sort((a, b) => b.RentalDate.CompareTo(a.RentalDate));
                return result.AsReadOnly();
            }
        }

        public IReadOnlyList<Rental> GetOverdue()
        {
            lock (_lock)
            {
                var result = new List<Rental>();
                foreach (var r in _rentals.Values)
                {
                    RefreshStatus(r);
                    if (r.Status == RentalStatus.Overdue)
                        result.Add(Clone(r));
                }
                result.Sort((a, b) => a.DueDate.CompareTo(b.DueDate));
                return result.AsReadOnly();
            }
        }

        public IReadOnlyList<Rental> Search(string query, RentalStatus? status)
        {
            lock (_lock)
            {
                bool hasQuery = !string.IsNullOrWhiteSpace(query);
                var result = new List<Rental>();

                foreach (var r in _rentals.Values)
                {
                    RefreshStatus(r);

                    if (hasQuery)
                    {
                        bool matchesCustomer = r.CustomerName != null &&
                            r.CustomerName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                        bool matchesMovie = r.MovieName != null &&
                            r.MovieName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

                        if (!matchesCustomer && !matchesMovie)
                            continue;
                    }

                    if (status.HasValue && r.Status != status.Value)
                        continue;

                    result.Add(Clone(r));
                }

                result.Sort((a, b) => b.RentalDate.CompareTo(a.RentalDate));
                return result.AsReadOnly();
            }
        }

        public Rental ReturnRental(int rentalId)
        {
            lock (_lock)
            {
                if (!_rentals.TryGetValue(rentalId, out var rental))
                    throw new KeyNotFoundException(
                        $"Rental with Id {rentalId} not found.");

                if (rental.Status == RentalStatus.Returned)
                    throw new InvalidOperationException(
                        $"Rental {rentalId} has already been returned.");

                rental.ReturnDate = DateTime.Today;
                rental.Status = RentalStatus.Returned;

                // Remove from rented set for O(1) availability checks
                _rentedMovieIds.Remove(rental.MovieId);

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

        /// <summary>
        /// O(1) movie availability check using the rented movie ID set.
        /// </summary>
        public bool IsMovieRentedOut(int movieId)
        {
            lock (_lock)
            {
                return _rentedMovieIds.Contains(movieId);
            }
        }

        public Rental Checkout(Rental rental)
        {
            if (rental == null)
                throw new ArgumentNullException(nameof(rental));

            lock (_lock)
            {
                // O(1) availability check via HashSet
                if (_rentedMovieIds.Contains(rental.MovieId))
                {
                    throw new InvalidOperationException(
                        "This movie is currently rented out.");
                }

                rental.Id = _nextId++;

                if (rental.DailyRate <= 0)
                    rental.DailyRate = DefaultDailyRate;

                if (rental.RentalDate == default)
                    rental.RentalDate = DateTime.Today;

                if (rental.DueDate == default)
                    rental.DueDate = rental.RentalDate.AddDays(DefaultRentalDays);

                rental.Status = RentalStatus.Active;
                rental.ReturnDate = null;
                rental.LateFee = 0;

                _rentals[rental.Id] = rental;
                _rentedMovieIds.Add(rental.MovieId);
                return Clone(rental);
            }
        }

        /// <summary>
        /// Computes rental statistics in a single pass over the collection,
        /// avoiding multiple separate Count()/Sum() enumerations.
        /// </summary>
        public RentalStats GetStats()
        {
            lock (_lock)
            {
                int active = 0, overdue = 0, returned = 0;
                decimal totalRevenue = 0, totalLateFees = 0;

                foreach (var r in _rentals.Values)
                {
                    RefreshStatus(r);

                    switch (r.Status)
                    {
                        case RentalStatus.Active:   active++;   break;
                        case RentalStatus.Overdue:  overdue++;  break;
                        case RentalStatus.Returned: returned++; break;
                    }

                    totalRevenue += r.TotalCost;
                    totalLateFees += r.LateFee;
                }

                return new RentalStats
                {
                    TotalRentals = _rentals.Count,
                    ActiveRentals = active,
                    OverdueRentals = overdue,
                    ReturnedRentals = returned,
                    TotalRevenue = totalRevenue,
                    TotalLateFees = totalLateFees
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
