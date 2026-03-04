using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Business logic for customer watchlists — smart prioritization,
    /// availability-aware suggestions, watchlist comparison, and
    /// genre-based insights built on top of the watchlist repository.
    /// </summary>
    public class WatchlistService
    {
        private readonly IWatchlistRepository _watchlistRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IRentalRepository _rentalRepository;

        public WatchlistService(
            IWatchlistRepository watchlistRepository,
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository,
            IRentalRepository rentalRepository)
        {
            _watchlistRepository = watchlistRepository
                ?? throw new ArgumentNullException(nameof(watchlistRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
        }

        /// <summary>
        /// Adds a movie to a customer's watchlist with validation.
        /// Auto-promotes new releases to High priority if no priority specified.
        /// </summary>
        public WatchlistItem AddToWatchlist(
            int customerId, int movieId,
            string note = null,
            WatchlistPriority? priority = null)
        {
            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new ArgumentException("Customer not found.", nameof(customerId));

            var movie = _movieRepository.GetById(movieId);
            if (movie == null)
                throw new ArgumentException("Movie not found.", nameof(movieId));

            if (_watchlistRepository.IsOnWatchlist(customerId, movieId))
                throw new InvalidOperationException(
                    $"\"{movie.Name}\" is already on {customer.Name}'s watchlist.");

            // Auto-promote new releases to High priority when none specified
            var effectivePriority = priority ?? WatchlistPriority.Normal;
            if (!priority.HasValue && movie.IsNewRelease)
                effectivePriority = WatchlistPriority.High;

            var item = new WatchlistItem
            {
                CustomerId = customerId,
                CustomerName = customer.Name,
                MovieId = movieId,
                MovieName = movie.Name,
                MovieGenre = movie.Genre,
                MovieRating = movie.Rating,
                AddedDate = DateTime.UtcNow,
                Note = note,
                Priority = effectivePriority
            };

            return _watchlistRepository.Add(item);
        }

        /// <summary>
        /// Gets a customer's watchlist with smart ordering:
        /// 1. MustWatch items first, then High, then Normal
        /// 2. Within each priority: available movies first, then by rating desc
        /// </summary>
        public IReadOnlyList<WatchlistRecommendation> GetSmartWatchlist(int customerId)
        {
            var items = _watchlistRepository.GetByCustomer(customerId);
            if (items.Count == 0)
                return Array.Empty<WatchlistRecommendation>();

            var rentals = _rentalRepository.GetAll();
            var activeRentalMovieIds = new HashSet<int>(
                rentals
                    .Where(r => r.ReturnDate == null)
                    .Select(r => r.MovieId));

            var recommendations = new List<WatchlistRecommendation>(items.Count);

            foreach (var item in items)
            {
                var movie = _movieRepository.GetById(item.MovieId);
                bool isAvailable = movie != null && !activeRentalMovieIds.Contains(item.MovieId);
                bool alreadyRented = rentals.Any(
                    r => r.CustomerId == customerId && r.MovieId == item.MovieId);

                recommendations.Add(new WatchlistRecommendation
                {
                    Item = item,
                    IsAvailable = isAvailable,
                    HasBeenRented = alreadyRented,
                    DaysOnWatchlist = (int)(DateTime.UtcNow - item.AddedDate).TotalDays,
                    IsNewRelease = movie?.IsNewRelease ?? false
                });
            }

            return recommendations
                .OrderByDescending(r => (int)r.Item.Priority)
                .ThenByDescending(r => r.IsAvailable)
                .ThenByDescending(r => r.Item.MovieRating ?? 0)
                .ToList()
                .AsReadOnly();
        }

        /// <summary>
        /// Finds movies that appear on multiple customers' watchlists
        /// (trending/popular demand) and returns them ranked.
        /// </summary>
        public IReadOnlyList<TrendingWatchlistMovie> GetTrendingMovies(int limit = 10)
        {
            if (limit < 1)
                throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be >= 1.");

            var popular = _watchlistRepository.GetMostWatchlisted(limit);
            var results = new List<TrendingWatchlistMovie>(popular.Count);

            foreach (var p in popular)
            {
                var movie = _movieRepository.GetById(p.MovieId);
                results.Add(new TrendingWatchlistMovie
                {
                    MovieId = p.MovieId,
                    MovieName = p.MovieName ?? movie?.Name ?? "Unknown",
                    WatchlistCount = p.WatchlistCount,
                    Genre = movie?.Genre,
                    Rating = movie?.Rating,
                    IsNewRelease = movie?.IsNewRelease ?? false
                });
            }

            return results.AsReadOnly();
        }

        /// <summary>
        /// Compares two customers' watchlists to find shared interests
        /// and unique picks. Useful for "friends who watch together" features.
        /// </summary>
        public WatchlistComparison CompareWatchlists(int customerIdA, int customerIdB)
        {
            if (customerIdA == customerIdB)
                throw new ArgumentException("Cannot compare a customer's watchlist with itself.");

            var customerA = _customerRepository.GetById(customerIdA);
            var customerB = _customerRepository.GetById(customerIdB);
            if (customerA == null)
                throw new ArgumentException("Customer A not found.", nameof(customerIdA));
            if (customerB == null)
                throw new ArgumentException("Customer B not found.", nameof(customerIdB));

            var itemsA = _watchlistRepository.GetByCustomer(customerIdA);
            var itemsB = _watchlistRepository.GetByCustomer(customerIdB);

            var movieIdsA = new HashSet<int>(itemsA.Select(i => i.MovieId));
            var movieIdsB = new HashSet<int>(itemsB.Select(i => i.MovieId));

            var sharedIds = new HashSet<int>(movieIdsA);
            sharedIds.IntersectWith(movieIdsB);

            var onlyA = movieIdsA.Except(sharedIds).ToList();
            var onlyB = movieIdsB.Except(sharedIds).ToList();

            return new WatchlistComparison
            {
                CustomerAName = customerA.Name,
                CustomerBName = customerB.Name,
                SharedMovies = sharedIds
                    .Select(id => _movieRepository.GetById(id))
                    .Where(m => m != null)
                    .Select(m => new WatchlistMovieSummary
                    {
                        MovieId = m.Id, MovieName = m.Name,
                        Genre = m.Genre, Rating = m.Rating
                    })
                    .ToList().AsReadOnly(),
                OnlyInA = onlyA
                    .Select(id => _movieRepository.GetById(id))
                    .Where(m => m != null)
                    .Select(m => new WatchlistMovieSummary
                    {
                        MovieId = m.Id, MovieName = m.Name,
                        Genre = m.Genre, Rating = m.Rating
                    })
                    .ToList().AsReadOnly(),
                OnlyInB = onlyB
                    .Select(id => _movieRepository.GetById(id))
                    .Where(m => m != null)
                    .Select(m => new WatchlistMovieSummary
                    {
                        MovieId = m.Id, MovieName = m.Name,
                        Genre = m.Genre, Rating = m.Rating
                    })
                    .ToList().AsReadOnly(),
                SimilarityScore = movieIdsA.Count + movieIdsB.Count > 0
                    ? (double)(sharedIds.Count * 2) / (movieIdsA.Count + movieIdsB.Count)
                    : 0.0
            };
        }

        /// <summary>
        /// Analyzes a customer's watchlist to surface genre preferences
        /// and suggest what to rent next based on patterns.
        /// </summary>
        public WatchlistInsights GetInsights(int customerId)
        {
            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new ArgumentException("Customer not found.", nameof(customerId));

            var items = _watchlistRepository.GetByCustomer(customerId);
            var stats = _watchlistRepository.GetStats(customerId);

            // Genre breakdown
            var genreCounts = items
                .Where(i => i.MovieGenre.HasValue)
                .GroupBy(i => i.MovieGenre.Value)
                .Select(g => new GenreCount { Genre = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToList();

            // Stale items (on watchlist > 30 days without being rented)
            var rentals = _rentalRepository.GetAll();
            var rentedMovieIds = new HashSet<int>(
                rentals.Where(r => r.CustomerId == customerId).Select(r => r.MovieId));

            var staleItems = items
                .Where(i => (DateTime.UtcNow - i.AddedDate).TotalDays > 30
                            && !rentedMovieIds.Contains(i.MovieId))
                .OrderBy(i => i.AddedDate)
                .ToList();

            // Top pick: highest priority unrented movie with best rating
            var topPick = items
                .Where(i => !rentedMovieIds.Contains(i.MovieId))
                .OrderByDescending(i => (int)i.Priority)
                .ThenByDescending(i => i.MovieRating ?? 0)
                .FirstOrDefault();

            // Average rating of watchlisted movies
            var ratedItems = items.Where(i => i.MovieRating.HasValue).ToList();
            double? avgRating = ratedItems.Count > 0
                ? ratedItems.Average(i => i.MovieRating.Value)
                : (double?)null;

            return new WatchlistInsights
            {
                CustomerName = customer.Name,
                TotalItems = stats.TotalItems,
                MustWatchCount = stats.MustWatchCount,
                HighPriorityCount = stats.HighCount,
                NormalCount = stats.NormalCount,
                GenreBreakdown = genreCounts.AsReadOnly(),
                TopGenre = genreCounts.FirstOrDefault()?.Genre,
                StaleItems = staleItems.AsReadOnly(),
                StaleCount = staleItems.Count,
                TopPick = topPick,
                AverageRating = avgRating.HasValue ? Math.Round(avgRating.Value, 2) : (double?)null,
                AlreadyRentedCount = items.Count(i => rentedMovieIds.Contains(i.MovieId))
            };
        }

        /// <summary>
        /// Moves a watchlist item to a different priority level.
        /// </summary>
        public void SetPriority(int watchlistItemId, WatchlistPriority newPriority)
        {
            var item = _watchlistRepository.GetById(watchlistItemId);
            if (item == null)
                throw new ArgumentException("Watchlist item not found.", nameof(watchlistItemId));

            item.Priority = newPriority;
            _watchlistRepository.Update(item);
        }

        /// <summary>
        /// Bulk-adds multiple movies to a customer's watchlist.
        /// Returns a result indicating which succeeded and which were skipped.
        /// </summary>
        public BulkAddResult BulkAdd(int customerId, IEnumerable<int> movieIds,
                                      WatchlistPriority priority = WatchlistPriority.Normal)
        {
            if (movieIds == null)
                throw new ArgumentNullException(nameof(movieIds));

            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new ArgumentException("Customer not found.", nameof(customerId));

            var result = new BulkAddResult();

            foreach (var movieId in movieIds)
            {
                var movie = _movieRepository.GetById(movieId);
                if (movie == null)
                {
                    result.NotFound.Add(movieId);
                    continue;
                }

                if (_watchlistRepository.IsOnWatchlist(customerId, movieId))
                {
                    result.AlreadyExists.Add(movieId);
                    continue;
                }

                var effectivePriority = priority;
                if (priority == WatchlistPriority.Normal && movie.IsNewRelease)
                    effectivePriority = WatchlistPriority.High;

                var item = new WatchlistItem
                {
                    CustomerId = customerId,
                    CustomerName = customer.Name,
                    MovieId = movieId,
                    MovieName = movie.Name,
                    MovieGenre = movie.Genre,
                    MovieRating = movie.Rating,
                    AddedDate = DateTime.UtcNow,
                    Priority = effectivePriority
                };

                _watchlistRepository.Add(item);
                result.Added.Add(movieId);
            }

            return result;
        }
    }

    // ── Result / DTO types ──────────────────────────────────────────

    /// <summary>
    /// A watchlist item enriched with availability and rental context.
    /// </summary>
    public class WatchlistRecommendation
    {
        public WatchlistItem Item { get; set; }
        public bool IsAvailable { get; set; }
        public bool HasBeenRented { get; set; }
        public int DaysOnWatchlist { get; set; }
        public bool IsNewRelease { get; set; }
    }

    /// <summary>
    /// A movie trending across multiple watchlists.
    /// </summary>
    public class TrendingWatchlistMovie
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public int WatchlistCount { get; set; }
        public Genre? Genre { get; set; }
        public int? Rating { get; set; }
        public bool IsNewRelease { get; set; }
    }

    /// <summary>
    /// Side-by-side comparison of two customers' watchlists.
    /// </summary>
    public class WatchlistComparison
    {
        public string CustomerAName { get; set; }
        public string CustomerBName { get; set; }
        public IReadOnlyList<WatchlistMovieSummary> SharedMovies { get; set; }
        public IReadOnlyList<WatchlistMovieSummary> OnlyInA { get; set; }
        public IReadOnlyList<WatchlistMovieSummary> OnlyInB { get; set; }

        /// <summary>
        /// Dice coefficient: 2 * |shared| / (|A| + |B|). Range [0, 1].
        /// </summary>
        public double SimilarityScore { get; set; }
    }

    public class WatchlistMovieSummary
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public Genre? Genre { get; set; }
        public int? Rating { get; set; }
    }

    /// <summary>
    /// Analytical insights about a customer's watchlist patterns.
    /// </summary>
    public class WatchlistInsights
    {
        public string CustomerName { get; set; }
        public int TotalItems { get; set; }
        public int MustWatchCount { get; set; }
        public int HighPriorityCount { get; set; }
        public int NormalCount { get; set; }
        public IReadOnlyList<GenreCount> GenreBreakdown { get; set; }
        public Genre? TopGenre { get; set; }
        public IReadOnlyList<WatchlistItem> StaleItems { get; set; }
        public int StaleCount { get; set; }
        public WatchlistItem TopPick { get; set; }
        public double? AverageRating { get; set; }
        public int AlreadyRentedCount { get; set; }
    }

    public class GenreCount
    {
        public Genre Genre { get; set; }
        public int Count { get; set; }
    }

    /// <summary>
    /// Result of a bulk watchlist add operation.
    /// </summary>
    public class BulkAddResult
    {
        public List<int> Added { get; set; } = new List<int>();
        public List<int> AlreadyExists { get; set; } = new List<int>();
        public List<int> NotFound { get; set; } = new List<int>();

        public int TotalProcessed => Added.Count + AlreadyExists.Count + NotFound.Count;
    }
}
