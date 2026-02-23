using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Review-specific repository with movie/customer query support.
    /// </summary>
    public interface IReviewRepository : IRepository<Review>
    {
        /// <summary>Returns all reviews for a movie, newest first.</summary>
        IReadOnlyList<Review> GetByMovie(int movieId);

        /// <summary>Returns all reviews by a customer, newest first.</summary>
        IReadOnlyList<Review> GetByCustomer(int customerId);

        /// <summary>
        /// Gets the existing review by this customer for this movie, or null.
        /// One review per customer per movie.
        /// </summary>
        Review GetByCustomerAndMovie(int customerId, int movieId);

        /// <summary>Returns true if the customer has already reviewed this movie.</summary>
        bool HasReviewed(int customerId, int movieId);

        /// <summary>Returns aggregate review statistics for a movie.</summary>
        ReviewStats GetMovieStats(int movieId);

        /// <summary>Returns the top-rated movies (by average review score) with at least minReviews.</summary>
        IReadOnlyList<MovieRating> GetTopRatedMovies(int count, int minReviews = 1);

        /// <summary>
        /// Searches reviews by customer name, movie name, or review text.
        /// Optionally filters by minimum star rating.
        /// </summary>
        IReadOnlyList<Review> Search(string query, int? minStars);
    }

    /// <summary>
    /// Aggregate stats for a movie's reviews.
    /// </summary>
    public class ReviewStats
    {
        public int MovieId { get; set; }
        public int TotalReviews { get; set; }
        public double AverageStars { get; set; }
        public int FiveStarCount { get; set; }
        public int FourStarCount { get; set; }
        public int ThreeStarCount { get; set; }
        public int TwoStarCount { get; set; }
        public int OneStarCount { get; set; }
    }

    /// <summary>
    /// A movie with its aggregated rating information.
    /// </summary>
    public class MovieRating
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public Genre? Genre { get; set; }
        public double AverageStars { get; set; }
        public int ReviewCount { get; set; }
    }
}
