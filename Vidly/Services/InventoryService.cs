using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Manages movie inventory — stock levels, availability checks,
    /// genre breakdowns, and availability forecasting based on rental data.
    /// 
    /// <para>Stock per title is configurable via <see cref="SetStock"/>
    /// or defaults to <see cref="DefaultCopiesPerTitle"/>.</para>
    /// </summary>
    public class InventoryService
    {
        /// <summary>Default number of copies per movie title.</summary>
        public const int DefaultCopiesPerTitle = 3;

        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly Dictionary<int, int> _stockOverrides;
        private readonly IClock _clock;

        /// <summary>
        /// Creates a new InventoryService.
        /// </summary>
        public InventoryService(
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            IClock clock)
        {
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _stockOverrides = new Dictionary<int, int>();
        }

        /// <summary>
        /// Set custom stock count for a specific movie.
        /// </summary>
        public void SetStock(int movieId, int copies)
        {
            if (copies < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(copies), "Stock count cannot be negative.");
            _stockOverrides[movieId] = copies;
        }

        /// <summary>
        /// Get the configured stock count for a movie.
        /// </summary>
        public int GetStockCount(int movieId)
        {
            return _stockOverrides.TryGetValue(movieId, out var count)
                ? count
                : DefaultCopiesPerTitle;
        }

        /// <summary>
        /// Get stock status for a single movie.
        /// </summary>
        public MovieStock GetMovieStock(int movieId)
        {
            var movie = _movieRepository.GetById(movieId);
            if (movie == null) return null;

            var rentals = _rentalRepository.GetAll();
            var activeForMovie = CountActiveRentals(rentals, movieId);
            var overdueForMovie = CountOverdueRentals(rentals, movieId);
            var earliestReturn = GetEarliestReturn(rentals, movieId);
            var totalCopies = GetStockCount(movieId);

            return new MovieStock
            {
                MovieId = movie.Id,
                MovieName = movie.Name,
                Genre = movie.Genre,
                TotalCopies = totalCopies,
                RentedCopies = activeForMovie,
                OverdueCopies = overdueForMovie,
                EarliestReturn = earliestReturn
            };
        }

        /// <summary>
        /// Get stock status for all movies in the catalog.
        /// </summary>
        public List<MovieStock> GetAllStock()
        {
            var movies = _movieRepository.GetAll();
            var rentals = _rentalRepository.GetAll();

            // Build rental counts in a single pass
            var activeCounts = new Dictionary<int, int>();
            var overdueCounts = new Dictionary<int, int>();
            var earliestReturns = new Dictionary<int, DateTime>();

            foreach (var r in rentals)
            {
                if (r.Status == RentalStatus.Returned) continue;

                activeCounts.TryGetValue(r.MovieId, out var _c1);
                activeCounts[r.MovieId] = _c1 + 1;

                if (r.DueDate < _clock.Today)
                {
                    overdueCounts.TryGetValue(r.MovieId, out var _c2);
                    overdueCounts[r.MovieId] = _c2 + 1;
                }

                if (!earliestReturns.ContainsKey(r.MovieId)
                    || r.DueDate < earliestReturns[r.MovieId])
                {
                    earliestReturns[r.MovieId] = r.DueDate;
                }
            }

            var result = new List<MovieStock>();
            foreach (var movie in movies)
            {
                activeCounts.TryGetValue(movie.Id, out var rented);
                overdueCounts.TryGetValue(movie.Id, out var overdue);
                earliestReturns.TryGetValue(movie.Id, out var earliest);

                result.Add(new MovieStock
                {
                    MovieId = movie.Id,
                    MovieName = movie.Name,
                    Genre = movie.Genre,
                    TotalCopies = GetStockCount(movie.Id),
                    RentedCopies = rented,
                    OverdueCopies = overdue,
                    EarliestReturn = rented > 0 ? earliest : (DateTime?)null
                });
            }

            result.Sort((a, b) =>
            {
                var levelCmp = StockLevelOrder(a.Level).CompareTo(StockLevelOrder(b.Level));
                if (levelCmp != 0) return levelCmp;
                return b.Utilization.CompareTo(a.Utilization);
            });

            return result;
        }

        /// <summary>
        /// Get movies filtered by stock level.
        /// </summary>
        public List<MovieStock> GetByStockLevel(StockLevel level)
        {
            return GetAllStock().Where(s => s.Level == level).ToList();
        }

        /// <summary>
        /// Check if a movie is available for rental.
        /// </summary>
        public bool IsAvailable(int movieId)
        {
            var stock = GetMovieStock(movieId);
            return stock != null && stock.IsAvailable;
        }

        /// <summary>
        /// Get overall inventory health summary.
        /// </summary>
        public InventorySummary GetSummary()
        {
            var allStock = GetAllStock();
            var rentals = _rentalRepository.GetAll();

            var summary = new InventorySummary
            {
                TotalTitles = allStock.Count,
                TotalCopies = allStock.Sum(s => s.TotalCopies),
                TotalRented = allStock.Sum(s => s.RentedCopies),
                OutOfStockTitles = allStock.Count(s => s.Level == StockLevel.OutOfStock),
                LowStockTitles = allStock.Count(s => s.Level == StockLevel.Low),
                TotalOverdue = allStock.Sum(s => s.OverdueCopies)
            };

            foreach (var r in rentals)
            {
                if (r.Status != RentalStatus.Returned && r.DueDate < _clock.Today)
                {
                    var daysOverdue = (int)Math.Ceiling(
                        (_clock.Today - r.DueDate).TotalDays);
                    summary.OverdueRevenue += daysOverdue * 1.50m;
                }
            }

            var genreGroups = new Dictionary<string, GenreStock>();
            foreach (var stock in allStock)
            {
                var genreName = stock.Genre?.ToString() ?? "Unknown";
                if (!genreGroups.TryGetValue(genreName, out var gs))
                {
                    gs = new GenreStock { GenreName = genreName };
                    genreGroups[genreName] = gs;
                }
                gs.TitleCount++;
                gs.TotalCopies += stock.TotalCopies;
                gs.RentedCopies += stock.RentedCopies;
            }

            summary.GenreBreakdown = genreGroups.Values
                .OrderByDescending(g => g.Utilization)
                .ToList();

            return summary;
        }

        /// <summary>
        /// Forecast availability for a movie over the next N days.
        /// 
        /// Overdue rentals (due date in the past but not returned) are treated as
        /// still rented on day 0 and assumed to be returned one day later.  The
        /// previous implementation counted overdue items as "returned by today",
        /// inflating predicted availability.
        /// </summary>
        public List<AvailabilityForecast> ForecastAvailability(int movieId, int days = 7)
        {
            if (days < 1 || days > 90)
                throw new ArgumentOutOfRangeException(
                    nameof(days), "Forecast window must be 1–90 days.");

            var movie = _movieRepository.GetById(movieId);
            if (movie == null) return new List<AvailabilityForecast>();

            var totalCopies = GetStockCount(movieId);
            var rentals = _rentalRepository.GetAll();
            var today = _clock.Today;

            // Collect expected return dates for active rentals.
            // For overdue rentals the due date is already past, so we push
            // their expected return to tomorrow (today + 1) — they are
            // still checked out and we can't count them as available today.
            var expectedReturns = new List<DateTime>();
            foreach (var r in rentals)
            {
                if (r.MovieId == movieId && r.Status != RentalStatus.Returned)
                {
                    var returnDate = r.DueDate < today ? today.AddDays(1) : r.DueDate;
                    expectedReturns.Add(returnDate);
                }
            }

            expectedReturns.Sort();

            var forecasts = new List<AvailabilityForecast>();
            for (int d = 0; d < days; d++)
            {
                var forecastDate = today.AddDays(d);
                var returnsBy = expectedReturns.Count(due => due <= forecastDate);
                var stillRented = expectedReturns.Count - returnsBy;
                var available = Math.Max(0, totalCopies - stillRented);

                forecasts.Add(new AvailabilityForecast
                {
                    Date = forecastDate,
                    PredictedAvailable = available,
                    ExpectedReturns = returnsBy
                });
            }

            return forecasts;
        }

        /// <summary>
        /// Get movies that need restocking — sorted by urgency.
        /// </summary>
        public List<MovieStock> GetRestockingNeeds(int limit = 10)
        {
            return GetAllStock()
                .Where(s => s.Level == StockLevel.OutOfStock || s.Level == StockLevel.Low)
                .Take(limit)
                .ToList();
        }

        private int CountActiveRentals(IReadOnlyList<Rental> rentals, int movieId)
        {
            int count = 0;
            foreach (var r in rentals)
                if (r.MovieId == movieId && r.Status != RentalStatus.Returned)
                    count++;
            return count;
        }

        private int CountOverdueRentals(IReadOnlyList<Rental> rentals, int movieId)
        {
            int count = 0;
            foreach (var r in rentals)
                if (r.MovieId == movieId && r.Status != RentalStatus.Returned
                    && r.DueDate < _clock.Today)
                    count++;
            return count;
        }

        private DateTime? GetEarliestReturn(IReadOnlyList<Rental> rentals, int movieId)
        {
            DateTime? earliest = null;
            foreach (var r in rentals)
            {
                if (r.MovieId == movieId && r.Status != RentalStatus.Returned)
                {
                    if (!earliest.HasValue || r.DueDate < earliest.Value)
                        earliest = r.DueDate;
                }
            }
            return earliest;
        }

        private static int StockLevelOrder(StockLevel level)
        {
            switch (level)
            {
                case StockLevel.OutOfStock: return 0;
                case StockLevel.Low: return 1;
                case StockLevel.Medium: return 2;
                case StockLevel.High: return 3;
                default: return 4;
            }
        }
    }
}
