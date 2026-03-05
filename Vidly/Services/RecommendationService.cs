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
        private readonly ITagRepository _tagRepository;

        /// <summary>
        /// Creates a RecommendationService with tag-based recommendation support.
        /// </summary>
        public RecommendationService(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository,
            ITagRepository tagRepository = null)
        {
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
            _tagRepository = tagRepository;
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

            // Load movies once and reuse for both genre analysis and scoring
            var allMovies = _movieRepository.GetAll();

            // Analyze genre preferences from rental history (reuses allMovies)
            var genrePreferences = AnalyzeGenrePreferences(customerRentals, allMovies);

            // Build tag affinity from rental history
            var tagAffinities = _tagRepository != null
                ? AnalyzeTagAffinities(customerRentals, _tagRepository)
                : new Dictionary<int, TagAffinity>();

            // Get staff pick movie IDs for boosting
            var staffPickMovieIds = _tagRepository != null
                ? GetStaffPickMovieIds(_tagRepository)
                : new HashSet<int>();

            // Score and rank unwatched movies
            var recommendations = ScoreMovies(
                    allMovies, rentedMovieIds, genrePreferences,
                    tagAffinities, staffPickMovieIds, _tagRepository)
                .Take(maxRecommendations)
                .ToList();

            return new RecommendationResult
            {
                CustomerId = customerId,
                TotalRentals = customerRentals.Count,
                GenrePreferences = BuildGenrePreferenceList(
                    genrePreferences, customerRentals, allMovies),
                TopTagAffinities = tagAffinities.Values
                    .OrderByDescending(ta => ta.Score)
                    .Take(5)
                    .ToList(),
                Recommendations = recommendations,
                TotalAvailableMovies = allMovies.Count(m => !rentedMovieIds.Contains(m.Id))
            };
        }

        /// <summary>
        /// Builds the genre preference list using a dictionary lookup instead of
        /// FirstOrDefault per rental per genre (O(G*R) → O(R) with O(1) lookups).
        /// </summary>
        internal static List<GenrePreference> BuildGenrePreferenceList(
            Dictionary<Genre, double> genrePreferences,
            IList<Rental> customerRentals,
            IReadOnlyList<Movie> allMovies)
        {
            var movieLookup = allMovies.ToDictionary(m => m.Id);

            // Single pass: count rentals per genre using O(1) dictionary lookups
            var rentalCounts = new Dictionary<Genre, int>();
            foreach (var rental in customerRentals)
            {
                if (movieLookup.TryGetValue(rental.MovieId, out var movie) && movie.Genre.HasValue)
                {
                    var genre = movie.Genre.Value;
                    if (rentalCounts.ContainsKey(genre))
                        rentalCounts[genre]++;
                    else
                        rentalCounts[genre] = 1;
                }
            }

            return genrePreferences
                .OrderByDescending(gp => gp.Value)
                .Select(gp => new GenrePreference
                {
                    Genre = gp.Key,
                    RentalCount = rentalCounts.TryGetValue(gp.Key, out var count) ? count : 0,
                    Score = gp.Value
                })
                .ToList();
        }

        /// <summary>
        /// Analyzes a customer's rental history to compute genre preference scores.
        /// Each rental of a genre adds 1.0 to the base score. More recent rentals
        /// get a recency bonus (up to +0.5 for rentals in the last 7 days).
        /// </summary>
        internal static Dictionary<Genre, double> AnalyzeGenrePreferences(
            IList<Rental> customerRentals,
            IReadOnlyList<Movie> allMovies)
        {
            var preferences = new Dictionary<Genre, double>();

            if (customerRentals == null || customerRentals.Count == 0)
                return preferences;

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
        /// Scores all unwatched movies based on genre preferences, tag affinities,
        /// staff pick status, and movie rating.
        /// </summary>
        internal static IEnumerable<MovieRecommendation> ScoreMovies(
            IReadOnlyList<Movie> allMovies,
            HashSet<int> rentedMovieIds,
            Dictionary<Genre, double> genrePreferences,
            Dictionary<int, TagAffinity> tagAffinities = null,
            HashSet<int> staffPickMovieIds = null,
            ITagRepository tagRepository = null)
        {
            var scored = new List<MovieRecommendation>();

            foreach (var movie in allMovies)
            {
                // Skip already-rented movies
                if (rentedMovieIds.Contains(movie.Id))
                    continue;

                double score = 0;
                var reasons = new List<string>();

                // Genre preference component
                double genreScore = 0;
                if (movie.Genre.HasValue && genrePreferences.TryGetValue(movie.Genre.Value, out genreScore))
                {
                    score += genreScore * 2.0;
                }

                // Tag affinity component: sum affinity scores for matching tags
                double tagScore = 0;
                if (tagAffinities != null && tagAffinities.Count > 0 && tagRepository != null)
                {
                    var movieTags = tagRepository.GetAssignmentsByMovie(movie.Id);
                    foreach (var tagAssignment in movieTags)
                    {
                        TagAffinity affinity;
                        if (tagAffinities.TryGetValue(tagAssignment.TagId, out affinity))
                        {
                            tagScore += affinity.Score;
                        }
                    }
                    score += tagScore * 1.5;

                    if (tagScore > 0)
                    {
                        var topMatchingTag = movieTags
                            .Where(a => tagAffinities.ContainsKey(a.TagId))
                            .OrderByDescending(a => tagAffinities[a.TagId].Score)
                            .FirstOrDefault();
                        if (topMatchingTag != null)
                            reasons.Add($"Because you liked movies tagged \"{topMatchingTag.TagName}\"");
                    }
                }

                // Staff pick boost (+2.0)
                bool isStaffPick = staffPickMovieIds != null && staffPickMovieIds.Contains(movie.Id);
                if (isStaffPick)
                {
                    score += 2.0;
                    reasons.Add("Staff Pick");
                }

                // Rating component
                double ratingScore = movie.Rating ?? 0;
                score += ratingScore;

                // Bonus for highly-rated movies (5 stars)
                if (movie.Rating.HasValue && movie.Rating.Value == 5)
                    score += 1.0;

                // Build recommendation reason
                string reason;
                if (reasons.Count > 0)
                {
                    // Tag/staff pick reasons take priority
                    if (genreScore > 0)
                        reasons.Insert(0, $"Matches your love of {movie.Genre}");
                    if (ratingScore >= 4)
                        reasons.Add($"highly rated ({movie.Rating}★)");
                    reason = string.Join(" · ", reasons);
                }
                else if (genreScore > 0 && ratingScore >= 4)
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

        /// <summary>
        /// Analyzes a customer's rental history to compute tag affinity scores.
        /// Each rental of a movie with a given tag adds 1.0, with recency bonus.
        /// </summary>
        internal static Dictionary<int, TagAffinity> AnalyzeTagAffinities(
            IList<Rental> customerRentals,
            ITagRepository tagRepository)
        {
            var affinities = new Dictionary<int, TagAffinity>();

            if (customerRentals == null || customerRentals.Count == 0 || tagRepository == null)
                return affinities;

            foreach (var rental in customerRentals)
            {
                var movieTags = tagRepository.GetAssignmentsByMovie(rental.MovieId);
                foreach (var tagAssignment in movieTags)
                {
                    TagAffinity aff;
                    if (!affinities.TryGetValue(tagAssignment.TagId, out aff))
                    {
                        aff = new TagAffinity
                        {
                            TagId = tagAssignment.TagId,
                            TagName = tagAssignment.TagName,
                            Score = 0,
                            RentalCount = 0
                        };
                        affinities[tagAssignment.TagId] = aff;
                    }

                    double score = 1.0;
                    var daysSinceRental = (DateTime.Today - rental.RentalDate).TotalDays;
                    if (daysSinceRental <= 30)
                        score += 0.5 * (1.0 - (daysSinceRental / 30.0));

                    aff.Score += score;
                    aff.RentalCount++;
                }
            }

            return affinities;
        }

        /// <summary>
        /// Gets the set of movie IDs tagged with staff-pick tags.
        /// </summary>
        internal static HashSet<int> GetStaffPickMovieIds(ITagRepository tagRepository)
        {
            var movieIds = new HashSet<int>();
            var allTags = tagRepository.GetAllTags(false);
            foreach (var tag in allTags)
            {
                if (!tag.IsStaffPick) continue;
                foreach (var a in tagRepository.GetAssignmentsByTag(tag.Id))
                    movieIds.Add(a.MovieId);
            }
            return movieIds;
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
        public List<TagAffinity> TopTagAffinities { get; set; } = new List<TagAffinity>();
        public List<MovieRecommendation> Recommendations { get; set; } = new List<MovieRecommendation>();
        public int TotalAvailableMovies { get; set; }
    }

    /// <summary>
    /// A customer's affinity for a specific tag, derived from rental history.
    /// </summary>
    public class TagAffinity
    {
        public int TagId { get; set; }
        public string TagName { get; set; }
        public double Score { get; set; }
        public int RentalCount { get; set; }
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
