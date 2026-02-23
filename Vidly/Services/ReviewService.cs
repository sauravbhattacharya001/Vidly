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

        public ReviewService(
            IReviewRepository reviewRepository,
            ICustomerRepository customerRepository,
            IMovieRepository movieRepository)
        {
            _reviewRepository = reviewRepository
                ?? throw new ArgumentNullException(nameof(reviewRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
        }

        /// <summary>
        /// Submits a new review, enforcing one-per-customer-per-movie.
        /// Enriches with customer/movie names.
        /// </summary>
        public Review SubmitReview(int customerId, int movieId, int stars, string reviewText)
        {
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
                CreatedDate = DateTime.Now,
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
        /// </summary>
        public ReviewSummary GetSummary()
        {
            var allReviews = _reviewRepository.GetAll();
            var movies = _movieRepository.GetAll();

            return new ReviewSummary
            {
                TotalReviews = allReviews.Count,
                AverageStars = allReviews.Count > 0
                    ? Math.Round(allReviews.Average(r => r.Stars), 1)
                    : 0,
                ReviewedMovieCount = allReviews.Select(r => r.MovieId).Distinct().Count(),
                TotalMovieCount = movies.Count,
                ReviewingCustomerCount = allReviews.Select(r => r.CustomerId).Distinct().Count(),
                StarDistribution = Enumerable.Range(1, 5)
                    .ToDictionary(s => s, s => allReviews.Count(r => r.Stars == s)),
                MostReviewedMovieId = allReviews
                    .GroupBy(r => r.MovieId)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key,
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
        /// </summary>
        private IReadOnlyList<Review> Enrich(IReadOnlyList<Review> reviews)
        {
            foreach (var review in reviews)
            {
                if (string.IsNullOrEmpty(review.CustomerName))
                {
                    var customer = _customerRepository.GetById(review.CustomerId);
                    review.CustomerName = customer?.Name ?? "Unknown";
                }
                if (string.IsNullOrEmpty(review.MovieName))
                {
                    var movie = _movieRepository.GetById(review.MovieId);
                    review.MovieName = movie?.Name ?? "Unknown";
                }
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
