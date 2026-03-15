using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Business logic for movie reviews — enrichment, stats, and moderation.
    /// </summary>
    public class ReviewService
    {
        private readonly IReviewRepository _reviewRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly IClock _clock;

        public ReviewService(
            IReviewRepository reviewRepository,
            ICustomerRepository customerRepository,
            IMovieRepository movieRepository,
            IClock clock = null)
        {
            _reviewRepository = reviewRepository
                ?? throw new ArgumentNullException(nameof(reviewRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _clock = clock ?? SystemClock.Instance;
        }

        /// <summary>
        /// Submits a new review, enforcing one-per-customer-per-movie.
        /// Enriches with customer/movie names.
        /// </summary>
        public Review SubmitReview(int customerId, int movieId, int stars, string reviewText)
        {
            // Input validation — the service is the domain boundary.
            // Data annotations on Review are only enforced by MVC model
            // binding; direct callers (APIs, other services) bypass them.
            if (stars < 1 || stars > 5)
                throw new ArgumentOutOfRangeException(
                    nameof(stars), stars,
                    "Rating must be between 1 and 5 stars.");

            if (reviewText != null && reviewText.Length > Review.MaxReviewTextLength)
                throw new ArgumentException(
                    $"Review text cannot exceed {Review.MaxReviewTextLength} characters.",
                    nameof(reviewText));

            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new ArgumentException("Customer not found.", nameof(customerId));

            var movie = _movieRepository.GetById(movieId);
            if (movie == null)
                throw new ArgumentException("Movie not found.", nameof(movieId));

            if (_reviewRepository.HasReviewed(customerId, movieId))
                throw new InvalidOperationException(
                    $"{customer.Name} has already reviewed \"{movie.Name}\".");

            var review = new Review
            {
                CustomerId = customerId,
                MovieId = movieId,
                Stars = stars,
                ReviewText = reviewText?.Trim(),
                CreatedDate = _clock.Now,
                CustomerName = customer.Name,
                MovieName = movie.Name,
            };

            _reviewRepository.Add(review);
            return review;
        }

        /// <summary>
        /// Returns all reviews for a movie, enriched with customer names.
        /// </summary>
        public IReadOnlyList<Review> GetMovieReviews(int movieId)
        {
            var reviews = _reviewRepository.GetByMovie(movieId);
            return Enrich(reviews);
        }

        /// <summary>
        /// Returns all reviews by a customer, enriched with movie names.
        /// </summary>
        public IReadOnlyList<Review> GetCustomerReviews(int customerId)
        {
            var reviews = _reviewRepository.GetByCustomer(customerId);
            return Enrich(reviews);
        }

        /// <summary>
        /// Returns review stats for a movie.
        /// </summary>
        public ReviewStats GetMovieStats(int movieId) =>
            _reviewRepository.GetMovieStats(movieId);

        /// <summary>
        /// Returns top-rated movies enriched with movie names and genres.
        /// </summary>
        public IReadOnlyList<MovieRating> GetTopRated(int count = 10, int minReviews = 1)
        {
            var ratings = _reviewRepository.GetTopRatedMovies(count, minReviews);
            foreach (var rating in ratings)
            {
                var movie = _movieRepository.GetById(rating.MovieId);
                if (movie != null)
                {
                    rating.MovieName = movie.Name;
                    rating.Genre = movie.Genre;
                }
            }
            return ratings;
        }

        /// <summary>
        /// Returns global review summary statistics.
        /// Single-pass: computes all metrics (star sum, distinct movies/customers,
        /// star distribution, most-reviewed movie) in one iteration over allReviews.
        /// Previous implementation used 8+ separate LINQ passes.
        /// </summary>
        public ReviewSummary GetSummary()
        {
            var allReviews = _reviewRepository.GetAll();
            var movies = _movieRepository.GetAll();

            if (allReviews.Count == 0)
            {
                return new ReviewSummary
                {
                    TotalReviews = 0,
                    AverageStars = 0,
                    ReviewedMovieCount = 0,
                    TotalMovieCount = movies.Count,
                    ReviewingCustomerCount = 0,
                    StarDistribution = Enumerable.Range(1, 5)
                        .ToDictionary(s => s, s => 0),
                    MostReviewedMovieId = null,
                };
            }

            long starSum = 0;
            var starCounts = new int[6]; // index 1-5 for star ratings
            var distinctMovies = new HashSet<int>();
            var distinctCustomers = new HashSet<int>();
            var movieRentalCounts = new Dictionary<int, int>();
            int topMovieId = 0;
            int topMovieCount = 0;

            foreach (var r in allReviews)
            {
                starSum += r.Stars;
                if (r.Stars >= 1 && r.Stars <= 5)
                    starCounts[r.Stars]++;

                distinctMovies.Add(r.MovieId);
                distinctCustomers.Add(r.CustomerId);

                if (!movieRentalCounts.TryGetValue(r.MovieId, out var count))
                    count = 0;
                count++;
                movieRentalCounts[r.MovieId] = count;

                if (count > topMovieCount)
                {
                    topMovieCount = count;
                    topMovieId = r.MovieId;
                }
            }

            return new ReviewSummary
            {
                TotalReviews = allReviews.Count,
                AverageStars = Math.Round((double)starSum / allReviews.Count, 1),
                ReviewedMovieCount = distinctMovies.Count,
                TotalMovieCount = movies.Count,
                ReviewingCustomerCount = distinctCustomers.Count,
                StarDistribution = Enumerable.Range(1, 5)
                    .ToDictionary(s => s, s => starCounts[s]),
                MostReviewedMovieId = topMovieId,
            };
        }

        /// <summary>
        /// Deletes a review by ID.
        /// </summary>
        public bool DeleteReview(int reviewId)
        {
            var review = _reviewRepository.GetById(reviewId);
            if (review == null)
                return false;

            _reviewRepository.Remove(reviewId);
            return true;
        }

        /// <summary>
        /// Enriches reviews with customer and movie display names.
        /// Pre-builds lookup dictionaries from unique IDs to avoid
        /// N+1 per-review GetById calls. O(R) instead of O(R × 2).
        /// </summary>
        public IReadOnlyList<Review> Enrich(IReadOnlyList<Review> reviews)
        {
            // Collect IDs that actually need enrichment
            var customerIdsNeeded = new HashSet<int>();
            var movieIdsNeeded = new HashSet<int>();
            foreach (var review in reviews)
            {
                if (string.IsNullOrEmpty(review.CustomerName))
                    customerIdsNeeded.Add(review.CustomerId);
                if (string.IsNullOrEmpty(review.MovieName))
                    movieIdsNeeded.Add(review.MovieId);
            }

            // Batch-build lookups (one call each instead of N)
            var customerNames = new Dictionary<int, string>();
            if (customerIdsNeeded.Count > 0)
            {
                foreach (var id in customerIdsNeeded)
                {
                    var customer = _customerRepository.GetById(id);
                    customerNames[id] = customer?.Name ?? "Unknown";
                }
            }

            var movieNames = new Dictionary<int, string>();
            if (movieIdsNeeded.Count > 0)
            {
                foreach (var id in movieIdsNeeded)
                {
                    var movie = _movieRepository.GetById(id);
                    movieNames[id] = movie?.Name ?? "Unknown";
                }
            }

            // Enrich from lookups — no repeated GetById for same customer/movie
            foreach (var review in reviews)
            {
                if (string.IsNullOrEmpty(review.CustomerName) &&
                    customerNames.TryGetValue(review.CustomerId, out var cName))
                    review.CustomerName = cName;

                if (string.IsNullOrEmpty(review.MovieName) &&
                    movieNames.TryGetValue(review.MovieId, out var mName))
                    review.MovieName = mName;
            }
            return reviews;
        }
    }

    /// <summary>
    /// Global review summary for the reviews index page.
    /// </summary>
    public class ReviewSummary
    {
        public int TotalReviews { get; set; }
        public double AverageStars { get; set; }
        public int ReviewedMovieCount { get; set; }
        public int TotalMovieCount { get; set; }
        public int ReviewingCustomerCount { get; set; }
        public Dictionary<int, int> StarDistribution { get; set; }
        public int? MostReviewedMovieId { get; set; }
    }
}
