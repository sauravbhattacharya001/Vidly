using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Generates personalized movie recommendations for customers based on
    /// their rental history. Analyzes genre preferences and suggests unwatched
    /// movies, prioritizing top-rated films in preferred genres.
    /// </summary>
    public class RecommendationService
    {
        private readonly IMovieRepository _movieRepository;
        private readonly IRentalRepository _rentalRepository;

        public RecommendationService(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository)
        {
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
        }

        /// <summary>
        /// Generates a recommendation result for a customer, including genre
        /// preferences analysis and scored movie suggestions.
        /// </summary>
        /// <param name="customerId">The customer to generate recommendations for.</param>
        /// <param name="maxRecommendations">Maximum number of movies to recommend (default 10).</param>
        /// <returns>A RecommendationResult with preferences and suggestions.</returns>
        public RecommendationResult GetRecommendations(int customerId, int maxRecommendations = 10)
        {
            if (maxRecommendations < 1)
                throw new ArgumentOutOfRangeException(nameof(maxRecommendations),
                    "Must request at least 1 recommendation.");

            var allRentals = _rentalRepository.GetAll();
            var customerRentals = allRentals.Where(r => r.CustomerId == customerId).ToList();

            // Build the set of movie IDs this customer has already rented
            var rentedMovieIds = new HashSet<int>(customerRentals.Select(r => r.MovieId));

            // Analyze genre preferences from rental history
            var genrePreferences = AnalyzeGenrePreferences(customerRentals, _movieRepository);

            // Get all available movies
            var allMovies = _movieRepository.GetAll();

            // Score and rank unwatched movies
            var recommendations = ScoreMovies(allMovies, rentedMovieIds, genrePreferences)
                .Take(maxRecommendations)
                .ToList();

            return new RecommendationResult
            {
                CustomerId = customerId,
                TotalRentals = customerRentals.Count,
                GenrePreferences = genrePreferences
                    .OrderByDescending(gp => gp.Value)
                    .Select(gp => new GenrePreference
                    {
                        Genre = gp.Key,
                        RentalCount = customerRentals.Count(r =>
                        {
                            var movie = allMovies.FirstOrDefault(m => m.Id == r.MovieId);
                            return movie?.Genre == gp.Key;
                        }),
                        Score = gp.Value
                    })
                    .ToList(),
                Recommendations = recommendations,
                TotalAvailableMovies = allMovies.Count(m => !rentedMovieIds.Contains(m.Id))
            };
        }

        /// <summary>
        /// Analyzes a customer's rental history to compute genre preference scores.
        /// Each rental of a genre adds 1.0 to the base score. More recent rentals
        /// get a recency bonus (up to +0.5 for rentals in the last 7 days).
        /// </summary>
        internal static Dictionary<Genre, double> AnalyzeGenrePreferences(
            IList<Rental> customerRentals,
            IMovieRepository movieRepository)
        {
            var preferences = new Dictionary<Genre, double>();

            if (customerRentals == null || customerRentals.Count == 0)
                return preferences;

            var allMovies = movieRepository.GetAll();
            var movieLookup = allMovies.ToDictionary(m => m.Id);

            foreach (var rental in customerRentals)
            {
                if (!movieLookup.TryGetValue(rental.MovieId, out var movie) || !movie.Genre.HasValue)
                    continue;

                var genre = movie.Genre.Value;

                if (!preferences.ContainsKey(genre))
                    preferences[genre] = 0;

                // Base score: 1.0 per rental
                double score = 1.0;

                // Recency bonus: up to +0.5 for recent rentals (within 30 days)
                var daysSinceRental = (DateTime.Today - rental.RentalDate).TotalDays;
                if (daysSinceRental <= 30)
                {
                    score += 0.5 * (1.0 - (daysSinceRental / 30.0));
                }

                preferences[genre] += score;
            }

            return preferences;
        }

        /// <summary>
        /// Scores all unwatched movies based on genre preferences and movie rating.
        /// Score = (genre preference score * 2) + (movie rating) + (rating bonus for 5-star movies).
        /// Movies with no genre preference still get base score from their rating.
        /// </summary>
        internal static IEnumerable<MovieRecommendation> ScoreMovies(
            IReadOnlyList<Movie> allMovies,
            HashSet<int> rentedMovieIds,
            Dictionary<Genre, double> genrePreferences)
        {
            var scored = new List<MovieRecommendation>();

            foreach (var movie in allMovies)
            {
                // Skip already-rented movies
                if (rentedMovieIds.Contains(movie.Id))
                    continue;

                double score = 0;
                string reason;

                // Genre preference component
                double genreScore = 0;
                if (movie.Genre.HasValue && genrePreferences.TryGetValue(movie.Genre.Value, out genreScore))
                {
                    score += genreScore * 2.0;
                }

                // Rating component
                double ratingScore = movie.Rating ?? 0;
                score += ratingScore;

                // Bonus for highly-rated movies (5 stars)
                if (movie.Rating.HasValue && movie.Rating.Value == 5)
                    score += 1.0;

                // Build recommendation reason
                if (genreScore > 0 && ratingScore >= 4)
                {
                    reason = $"Matches your love of {movie.Genre} + highly rated ({movie.Rating}★)";
                }
                else if (genreScore > 0)
                {
                    reason = $"Based on your interest in {movie.Genre} movies";
                }
                else if (ratingScore >= 4)
                {
                    reason = $"Highly rated ({movie.Rating}★) — try something new!";
                }
                else
                {
                    reason = "Explore a different genre";
                }

                scored.Add(new MovieRecommendation
                {
                    Movie = movie,
                    Score = Math.Round(score, 2),
                    Reason = reason
                });
            }

            // Sort by score descending, then by rating descending, then by name
            return scored
                .OrderByDescending(r => r.Score)
                .ThenByDescending(r => r.Movie.Rating ?? 0)
                .ThenBy(r => r.Movie.Name);
        }
    }

    /// <summary>
    /// Complete recommendation result for a customer.
    /// </summary>
    public class RecommendationResult
    {
        public int CustomerId { get; set; }
        public int TotalRentals { get; set; }
        public List<GenrePreference> GenrePreferences { get; set; } = new List<GenrePreference>();
        public List<MovieRecommendation> Recommendations { get; set; } = new List<MovieRecommendation>();
        public int TotalAvailableMovies { get; set; }
    }

    /// <summary>
    /// A customer's affinity for a specific genre.
    /// </summary>
    public class GenrePreference
    {
        public Genre Genre { get; set; }
        public int RentalCount { get; set; }
        public double Score { get; set; }
    }

    /// <summary>
    /// A scored movie recommendation with explanation.
    /// </summary>
    public class MovieRecommendation
    {
        public Movie Movie { get; set; }
        public double Score { get; set; }
        public string Reason { get; set; }
    }
}
