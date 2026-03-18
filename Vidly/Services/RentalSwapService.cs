using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Result of a swap operation.
    /// </summary>
    public class SwapResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public Rental OldRental { get; set; }
        public Rental NewRental { get; set; }
        public decimal SwapFee { get; set; }
        public decimal RateDifference { get; set; }
        public int RemainingDays { get; set; }

        public static SwapResult Failed(string error) => new SwapResult { Success = false, Error = error };
    }

    /// <summary>
    /// Swap quote shown to the customer before confirming.
    /// </summary>
    public class SwapQuote
    {
        public Rental CurrentRental { get; set; }
        public Movie NewMovie { get; set; }
        public int RemainingDays { get; set; }
        public decimal CurrentDailyRate { get; set; }
        public decimal NewDailyRate { get; set; }
        public decimal RateDifference { get; set; }
        public decimal SwapFee { get; set; }
        public decimal TotalExtraCost { get; set; }
        public bool IsUpgrade { get; set; }
        public string Summary { get; set; }
    }

    /// <summary>
    /// History entry for a completed swap.
    /// </summary>
    public class SwapRecord
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int OldRentalId { get; set; }
        public int NewRentalId { get; set; }
        public string OldMovieName { get; set; }
        public string NewMovieName { get; set; }
        public decimal SwapFee { get; set; }
        public decimal RateDifference { get; set; }
        public DateTime SwapDate { get; set; }
    }

    /// <summary>
    /// Allows customers to swap an active rental for a different movie.
    /// 
    /// Rules:
    /// - Only active (non-returned, non-overdue) rentals can be swapped
    /// - The new movie must be available (not rented out)
    /// - Remaining rental days transfer to the new movie
    /// - A flat $1.99 swap fee applies
    /// - If the new movie has a higher daily rate, the customer pays the difference
    ///   for the remaining days; if lower, no refund (keeps it simple)
    /// - Each rental can only be swapped once
    /// - Swap history is tracked for analytics
    /// </summary>
    public class RentalSwapService
    {
        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;

        /// <summary>Flat fee charged per swap.</summary>
        public const decimal SwapFee = 1.99m;

        /// <summary>Maximum swaps per customer per day.</summary>
        public const int MaxSwapsPerDay = 3;

        private static readonly List<SwapRecord> _swapHistory = new List<SwapRecord>();
        private static readonly HashSet<int> _swappedRentalIds = new HashSet<int>();
        private static readonly object _lock = new object();
        private static int _nextSwapId = 1;

        public RentalSwapService(
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository)
        {
            _rentalRepository = rentalRepository ?? throw new ArgumentNullException(nameof(rentalRepository));
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        }

        /// <summary>
        /// Get a quote for swapping an active rental to a different movie.
        /// </summary>
        public SwapQuote GetQuote(int rentalId, int newMovieId)
        {
            var rental = _rentalRepository.GetById(rentalId);
            if (rental == null)
                throw new ArgumentException($"Rental {rentalId} not found.", nameof(rentalId));

            var newMovie = _movieRepository.GetById(newMovieId);
            if (newMovie == null)
                throw new ArgumentException($"Movie {newMovieId} not found.", nameof(newMovieId));

            if (rental.Status != RentalStatus.Active)
                throw new InvalidOperationException("Only active rentals can be swapped.");

            if (rental.MovieId == newMovieId)
                throw new InvalidOperationException("Cannot swap a movie for itself.");

            var remainingDays = Math.Max(1, (int)Math.Ceiling((rental.DueDate - DateTime.Today).TotalDays));
            var currentRate = rental.DailyRate;
            var newRate = newMovie.DailyRate ?? (newMovie.IsNewRelease ? PricingService.NewReleaseDailyRate : PricingService.DefaultDailyRate);
            var rateDiff = Math.Max(0, (newRate - currentRate) * remainingDays);
            var totalExtra = SwapFee + rateDiff;

            return new SwapQuote
            {
                CurrentRental = rental,
                NewMovie = newMovie,
                RemainingDays = remainingDays,
                CurrentDailyRate = currentRate,
                NewDailyRate = newRate,
                RateDifference = rateDiff,
                SwapFee = SwapFee,
                TotalExtraCost = totalExtra,
                IsUpgrade = newRate > currentRate,
                Summary = rateDiff > 0
                    ? $"Swap fee: ${SwapFee:F2} + rate upgrade: ${rateDiff:F2} = ${totalExtra:F2} total"
                    : $"Swap fee: ${SwapFee:F2} (same or lower rate — no extra charge)"
            };
        }

        /// <summary>
        /// Execute a swap: return the old movie and create a new rental for the replacement.
        /// </summary>
        public SwapResult ExecuteSwap(int rentalId, int newMovieId)
        {
            lock (_lock)
            {
                // Validate rental
                var rental = _rentalRepository.GetById(rentalId);
                if (rental == null)
                    return SwapResult.Failed("Rental not found.");

                if (rental.Status != RentalStatus.Active)
                    return SwapResult.Failed("Only active rentals can be swapped.");

                if (rental.IsOverdue)
                    return SwapResult.Failed("Overdue rentals cannot be swapped. Please return first.");

                if (rental.MovieId == newMovieId)
                    return SwapResult.Failed("Cannot swap a movie for itself.");

                if (_swappedRentalIds.Contains(rentalId))
                    return SwapResult.Failed("This rental has already been swapped once.");

                // Check daily swap limit
                var todaySwaps = _swapHistory.Count(s =>
                    s.CustomerId == rental.CustomerId &&
                    s.SwapDate.Date == DateTime.Today);
                if (todaySwaps >= MaxSwapsPerDay)
                    return SwapResult.Failed($"Daily swap limit reached ({MaxSwapsPerDay} per day).");

                // Validate new movie
                var newMovie = _movieRepository.GetById(newMovieId);
                if (newMovie == null)
                    return SwapResult.Failed("New movie not found.");

                if (_rentalRepository.IsMovieRentedOut(newMovieId))
                    return SwapResult.Failed($"\"{newMovie.Name}\" is currently rented out.");

                // Calculate swap economics
                var remainingDays = Math.Max(1, (int)Math.Ceiling((rental.DueDate - DateTime.Today).TotalDays));
                var newRate = newMovie.DailyRate ?? (newMovie.IsNewRelease ? PricingService.NewReleaseDailyRate : PricingService.DefaultDailyRate);
                var rateDiff = Math.Max(0, (newRate - rental.DailyRate) * remainingDays);

                // Return the old rental
                _rentalRepository.ReturnRental(rentalId);

                // Create a new rental with the remaining days
                var customer = _customerRepository.GetById(rental.CustomerId);
                var newRental = new Rental
                {
                    CustomerId = rental.CustomerId,
                    CustomerName = customer?.Name ?? rental.CustomerName,
                    MovieId = newMovieId,
                    MovieName = newMovie.Name,
                    RentalDate = DateTime.Today,
                    DueDate = DateTime.Today.AddDays(remainingDays),
                    DailyRate = newRate,
                    Status = RentalStatus.Active
                };
                _rentalRepository.Add(newRental);

                // Track the swap
                _swappedRentalIds.Add(rentalId);
                _swappedRentalIds.Add(newRental.Id);

                var record = new SwapRecord
                {
                    Id = _nextSwapId++,
                    CustomerId = rental.CustomerId,
                    CustomerName = customer?.Name ?? rental.CustomerName,
                    OldRentalId = rentalId,
                    NewRentalId = newRental.Id,
                    OldMovieName = rental.MovieName,
                    NewMovieName = newMovie.Name,
                    SwapFee = SwapFee,
                    RateDifference = rateDiff,
                    SwapDate = DateTime.Now
                };
                _swapHistory.Add(record);

                return new SwapResult
                {
                    Success = true,
                    OldRental = rental,
                    NewRental = newRental,
                    SwapFee = SwapFee,
                    RateDifference = rateDiff,
                    RemainingDays = remainingDays
                };
            }
        }

        /// <summary>
        /// Get eligible movies a customer can swap to (available, not the current one).
        /// </summary>
        public IReadOnlyList<Movie> GetSwapCandidates(int rentalId)
        {
            var rental = _rentalRepository.GetById(rentalId);
            if (rental == null)
                return new List<Movie>();

            return _movieRepository.GetAll()
                .Where(m => m.Id != rental.MovieId && !_rentalRepository.IsMovieRentedOut(m.Id))
                .OrderBy(m => m.Name)
                .ToList();
        }

        /// <summary>
        /// Get swap history for a customer.
        /// </summary>
        public IReadOnlyList<SwapRecord> GetCustomerSwapHistory(int customerId)
        {
            lock (_lock)
            {
                return _swapHistory
                    .Where(s => s.CustomerId == customerId)
                    .OrderByDescending(s => s.SwapDate)
                    .ToList();
            }
        }

        /// <summary>
        /// Get all swap history (admin view).
        /// </summary>
        public IReadOnlyList<SwapRecord> GetAllSwapHistory()
        {
            lock (_lock)
            {
                return _swapHistory.OrderByDescending(s => s.SwapDate).ToList();
            }
        }

        /// <summary>
        /// Check if a rental was created via swap.
        /// </summary>
        public bool IsSwappedRental(int rentalId)
        {
            lock (_lock)
            {
                return _swappedRentalIds.Contains(rentalId);
            }
        }

        /// <summary>
        /// Get swap statistics.
        /// </summary>
        public SwapStats GetStats()
        {
            lock (_lock)
            {
                var totalSwaps = _swapHistory.Count;
                var totalFees = _swapHistory.Sum(s => s.SwapFee);
                var totalUpgrades = _swapHistory.Sum(s => s.RateDifference);
                var todaySwaps = _swapHistory.Count(s => s.SwapDate.Date == DateTime.Today);

                // Most swapped-from and swapped-to movies
                var topFrom = _swapHistory
                    .GroupBy(s => s.OldMovieName)
                    .OrderByDescending(g => g.Count())
                    .Select(g => new { Movie = g.Key, Count = g.Count() })
                    .FirstOrDefault();

                var topTo = _swapHistory
                    .GroupBy(s => s.NewMovieName)
                    .OrderByDescending(g => g.Count())
                    .Select(g => new { Movie = g.Key, Count = g.Count() })
                    .FirstOrDefault();

                return new SwapStats
                {
                    TotalSwaps = totalSwaps,
                    TotalSwapFees = totalFees,
                    TotalUpgradeRevenue = totalUpgrades,
                    TotalRevenue = totalFees + totalUpgrades,
                    SwapsToday = todaySwaps,
                    MostSwappedFromMovie = topFrom?.Movie,
                    MostSwappedToMovie = topTo?.Movie
                };
            }
        }
    }

    /// <summary>
    /// Swap statistics for the admin dashboard.
    /// </summary>
    public class SwapStats
    {
        public int TotalSwaps { get; set; }
        public decimal TotalSwapFees { get; set; }
        public decimal TotalUpgradeRevenue { get; set; }
        public decimal TotalRevenue { get; set; }
        public int SwapsToday { get; set; }
        public string MostSwappedFromMovie { get; set; }
        public string MostSwappedToMovie { get; set; }
    }
}
