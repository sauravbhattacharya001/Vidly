using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Thread-safe in-memory review repository with one-review-per-customer-per-movie
    /// constraint enforced atomically.
    /// </summary>
    public class InMemoryReviewRepository : IReviewRepository
    {
        private static readonly Dictionary<int, Review> _reviews = new Dictionary<int, Review>();

        /// <summary>
        /// Composite key set for O(1) duplicate detection.
        /// Key format: "customerId:movieId".
        /// </summary>
        private static readonly HashSet<string> _customerMovieKeys = new HashSet<string>();

        /// <summary>
        /// Maps composite key "customerId:movieId" → review ID for O(1) lookup.
        /// </summary>
        private static readonly Dictionary<string, int> _reviewByCompositeKey = new Dictionary<string, int>();

        private static readonly object _lock = new object();
        private static int _nextId = 1;

        private static string CompositeKey(int customerId, int movieId) =>
            $"{customerId}:{movieId}";

        public Review GetById(int id)
        {
            lock (_lock)
            {
                return _reviews.TryGetValue(id, out var review) ? Clone(review) : null;
            }
        }

        public IReadOnlyList<Review> GetAll()
        {
            lock (_lock)
            {
                return _reviews.Values
                    .OrderByDescending(r => r.CreatedDate)
                    .Select(Clone)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public void Add(Review review)
        {
            if (review == null)
                throw new ArgumentNullException(nameof(review));

            lock (_lock)
            {
                string key = CompositeKey(review.CustomerId, review.MovieId);
                if (_customerMovieKeys.Contains(key))
                    throw new InvalidOperationException(
                        "This customer has already reviewed this movie.");

                review.Id = _nextId++;
                if (review.CreatedDate == default)
                    review.CreatedDate = DateTime.Now;

                _reviews[review.Id] = review;
                _customerMovieKeys.Add(key);
                _reviewByCompositeKey[key] = review.Id;
            }
        }

        public void Update(Review review)
        {
            if (review == null)
                throw new ArgumentNullException(nameof(review));

            lock (_lock)
            {
                if (!_reviews.ContainsKey(review.Id))
                    throw new KeyNotFoundException($"Review {review.Id} not found.");

                _reviews[review.Id] = review;
            }
        }

        public void Remove(int id)
        {
            lock (_lock)
            {
                if (_reviews.TryGetValue(id, out var review))
                {
                    var compositeKey = CompositeKey(review.CustomerId, review.MovieId);
                    _customerMovieKeys.Remove(compositeKey);
                    _reviewByCompositeKey.Remove(compositeKey);
                    _reviews.Remove(id);
                }
            }
        }

        public IReadOnlyList<Review> GetByMovie(int movieId)
        {
            lock (_lock)
            {
                return _reviews.Values
                    .Where(r => r.MovieId == movieId)
                    .OrderByDescending(r => r.CreatedDate)
                    .Select(Clone)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public IReadOnlyList<Review> GetByCustomer(int customerId)
        {
            lock (_lock)
            {
                return _reviews.Values
                    .Where(r => r.CustomerId == customerId)
                    .OrderByDescending(r => r.CreatedDate)
                    .Select(Clone)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public Review GetByCustomerAndMovie(int customerId, int movieId)
        {
            lock (_lock)
            {
                string key = CompositeKey(customerId, movieId);
                if (_reviewByCompositeKey.TryGetValue(key, out var reviewId) &&
                    _reviews.TryGetValue(reviewId, out var review))
                {
                    return Clone(review);
                }
                return null;
            }
        }

        public bool HasReviewed(int customerId, int movieId)
        {
            lock (_lock)
            {
                return _customerMovieKeys.Contains(CompositeKey(customerId, movieId));
            }
        }

        public ReviewStats GetMovieStats(int movieId)
        {
            lock (_lock)
            {
                var movieReviews = _reviews.Values
                    .Where(r => r.MovieId == movieId)
                    .ToList();

                return new ReviewStats
                {
                    MovieId = movieId,
                    TotalReviews = movieReviews.Count,
                    AverageStars = movieReviews.Count > 0
                        ? Math.Round(movieReviews.Average(r => r.Stars), 1)
                        : 0,
                    FiveStarCount = movieReviews.Count(r => r.Stars == 5),
                    FourStarCount = movieReviews.Count(r => r.Stars == 4),
                    ThreeStarCount = movieReviews.Count(r => r.Stars == 3),
                    TwoStarCount = movieReviews.Count(r => r.Stars == 2),
                    OneStarCount = movieReviews.Count(r => r.Stars == 1),
                };
            }
        }

        public IReadOnlyList<MovieRating> GetTopRatedMovies(int count, int minReviews = 1)
        {
            lock (_lock)
            {
                return _reviews.Values
                    .GroupBy(r => r.MovieId)
                    .Where(g => g.Count() >= minReviews)
                    .Select(g => new MovieRating
                    {
                        MovieId = g.Key,
                        AverageStars = Math.Round(g.Average(r => r.Stars), 1),
                        ReviewCount = g.Count(),
                    })
                    .OrderByDescending(mr => mr.AverageStars)
                    .ThenByDescending(mr => mr.ReviewCount)
                    .Take(count)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public IReadOnlyList<Review> Search(string query, int? minStars)
        {
            lock (_lock)
            {
                var results = _reviews.Values.AsEnumerable();

                if (minStars.HasValue)
                    results = results.Where(r => r.Stars >= minStars.Value);

                if (!string.IsNullOrWhiteSpace(query))
                {
                    var q = query.Trim();
                    results = results.Where(r =>
                        (r.CustomerName != null && r.CustomerName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (r.MovieName != null && r.MovieName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (r.ReviewText != null && r.ReviewText.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));
                }

                return results
                    .OrderByDescending(r => r.CreatedDate)
                    .Select(Clone)
                    .ToList()
                    .AsReadOnly();
            }
        }

        /// <summary>
        /// Resets the repository for test isolation.
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _reviews.Clear();
                _customerMovieKeys.Clear();
                _reviewByCompositeKey.Clear();
                _nextId = 1;
            }
        }

        /// <summary>
        /// Alias for Reset — reviews have no seed data by default.
        /// </summary>
        public static void ResetEmpty()
        {
            Reset();
        }

        private static Review Clone(Review r) => new Review
        {
            Id = r.Id,
            CustomerId = r.CustomerId,
            MovieId = r.MovieId,
            Stars = r.Stars,
            ReviewText = r.ReviewText,
            CreatedDate = r.CreatedDate,
            CustomerName = r.CustomerName,
            MovieName = r.MovieName,
        };
    }
}
