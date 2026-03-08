using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Generates personalized movie recommendations for customers based on
    /// their rental history. Analyzes genre preferences and tag affinities,
    /// then suggests unwatched movies prioritizing highly-rated films in
    /// preferred genres/tags.
    ///
    /// Performance: all tag assignments are bulk-loaded once via
    /// GetAllAssignments() and indexed by movieId, eliminating
    /// per-movie / per-rental N+1 repository calls.
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

            // Load movies once — shared by genre analysis, preference list, and scoring
            var allMovies = _movieRepository.GetAll();
            var movieLookup = BuildMovieLookup(allMovies);

            // Analyze genre preferences from rental history
            var genrePreferences = AnalyzeGenrePreferences(customerRentals, movieLookup);

            // Bulk-load all tag assignments once (replaces per-movie/per-rental
            // GetAssignmentsByMovie calls that caused N+1 query pattern)
            Dictionary<int, List<MovieTagAssignment>> tagsByMovie = null;
            var tagAffinities = new Dictionary<int, TagAffinity>();
            var staffPickMovieIds = new HashSet<int>();

            if (_tagRepository != null)
            {
                tagsByMovie = BuildTagIndex(_tagRepository.GetAllAssignments());
                tagAffinities = AnalyzeTagAffinities(customerRentals, tagsByMovie);
                staffPickMovieIds = GetStaffPickMovieIds(_tagRepository, tagsByMovie);
            }

            // Score and rank unwatched movies
            var recommendations = ScoreMovies(
                    allMovies, rentedMovieIds, genrePreferences,
                    tagAffinities, staffPickMovieIds, tagsByMovie)
                .Take(maxRecommendations)
                .ToList();

            return new RecommendationResult
            {
                CustomerId = customerId,
                TotalRentals = customerRentals.Count,
                GenrePreferences = BuildGenrePreferenceList(
                    genrePreferences, customerRentals, movieLookup),
                TopTagAffinities = tagAffinities.Values
                    .OrderByDescending(ta => ta.Score)
                    .Take(5)
                    .ToList(),
                Recommendations = recommendations,
                TotalAvailableMovies = allMovies.Count(m => !rentedMovieIds.Contains(m.Id))
            };
        }

        // ── Index builders ───────────────────────────────────────

        /// <summary>
        /// Builds a dictionary of movieId → Movie for O(1) lookups.
        /// Shared across genre analysis, preference list building, and scoring.
        /// Previously built separately in AnalyzeGenrePreferences and
        /// BuildGenrePreferenceList (redundant O(M) work).
        /// </summary>
        internal static Dictionary<int, Movie> BuildMovieLookup(IReadOnlyList<Movie> allMovies)
        {
            var lookup = new Dictionary<int, Movie>(allMovies.Count);
            foreach (var m in allMovies)
                lookup[m.Id] = m;
            return lookup;
        }

        /// <summary>
        /// Indexes all tag assignments by movieId for O(1) lookups per movie.
        /// Replaces the N+1 pattern of calling GetAssignmentsByMovie() for
        /// each movie/rental — a single bulk load + dictionary build.
        /// </summary>
        internal static Dictionary<int, List<MovieTagAssignment>> BuildTagIndex(
            IReadOnlyList<MovieTagAssignment> allAssignments)
        {
            var index = new Dictionary<int, List<MovieTagAssignment>>();
            foreach (var a in allAssignments)
            {
                List<MovieTagAssignment> list;
                if (!index.TryGetValue(a.MovieId, out list))
                {
                    list = new List<MovieTagAssignment>();
                    index[a.MovieId] = list;
                }
                list.Add(a);
            }
            return index;
        }

        // ── Genre preferences ────────────────────────────────────

        /// <summary>
        /// Builds the genre preference list using the shared movieLookup.
        /// Single pass over rentals to count per-genre.
        /// </summary>
        internal static List<GenrePreference> BuildGenrePreferenceList(
            Dictionary<Genre, double> genrePreferences,
            IList<Rental> customerRentals,
            Dictionary<int, Movie> movieLookup)
        {
            // Single pass: count rentals per genre using O(1) dictionary lookups
            var rentalCounts = new Dictionary<Genre, int>();
            foreach (var rental in customerRentals)
            {
                Movie movie;
                if (movieLookup.TryGetValue(rental.MovieId, out movie) && movie.Genre.HasValue)
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
        /// get a recency bonus (up to +0.5 for rentals in the last 30 days).
        /// Accepts pre-built movieLookup to avoid redundant dictionary creation.
        /// </summary>
        internal static Dictionary<Genre, double> AnalyzeGenrePreferences(
            IList<Rental> customerRentals,
            Dictionary<int, Movie> movieLookup)
        {
            var preferences = new Dictionary<Genre, double>();

            if (customerRentals == null || customerRentals.Count == 0)
                return preferences;

            foreach (var rental in customerRentals)
            {
                Movie movie;
                if (!movieLookup.TryGetValue(rental.MovieId, out movie) || !movie.Genre.HasValue)
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

        // ── Tag affinities ───────────────────────────────────────

        /// <summary>
        /// Analyzes tag affinities using the pre-built tag index.
        /// Previously called GetAssignmentsByMovie per rental (N+1);
        /// now uses O(1) dictionary lookups per rental.
        /// </summary>
        internal static Dictionary<int, TagAffinity> AnalyzeTagAffinities(
            IList<Rental> customerRentals,
            Dictionary<int, List<MovieTagAssignment>> tagsByMovie)
        {
            var affinities = new Dictionary<int, TagAffinity>();

            if (customerRentals == null || customerRentals.Count == 0 || tagsByMovie == null)
                return affinities;

            foreach (var rental in customerRentals)
            {
                List<MovieTagAssignment> movieTags;
                if (!tagsByMovie.TryGetValue(rental.MovieId, out movieTags))
                    continue;

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
        /// Uses the pre-built tag index instead of per-tag GetAssignmentsByTag calls.
        /// </summary>
        internal static HashSet<int> GetStaffPickMovieIds(
            ITagRepository tagRepository,
            Dictionary<int, List<MovieTagAssignment>> tagsByMovie)
        {
            var staffPickTagIds = new HashSet<int>();
            var allTags = tagRepository.GetAllTags(false);
            foreach (var tag in allTags)
            {
                if (tag.IsStaffPick)
                    staffPickTagIds.Add(tag.Id);
            }

            var movieIds = new HashSet<int>();
            foreach (var kvp in tagsByMovie)
            {
                foreach (var a in kvp.Value)
                {
                    if (staffPickTagIds.Contains(a.TagId))
                    {
                        movieIds.Add(kvp.Key);
                        break; // one staff-pick tag is enough
                    }
                }
            }
            return movieIds;
        }

        // ── Scoring ──────────────────────────────────────────────

        /// <summary>
        /// Scores all unwatched movies based on genre preferences, tag affinities,
        /// staff pick status, and movie rating.
        /// Uses pre-built tagsByMovie index for O(1) per-movie tag lookups
        /// instead of calling tagRepository.GetAssignmentsByMovie() per movie.
        /// </summary>
        internal static IEnumerable<MovieRecommendation> ScoreMovies(
            IReadOnlyList<Movie> allMovies,
            HashSet<int> rentedMovieIds,
            Dictionary<Genre, double> genrePreferences,
            Dictionary<int, TagAffinity> tagAffinities = null,
            HashSet<int> staffPickMovieIds = null,
            Dictionary<int, List<MovieTagAssignment>> tagsByMovie = null)
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
                // Uses pre-built index — O(1) lookup per movie instead of repository call
                double tagScore = 0;
                if (tagAffinities != null && tagAffinities.Count > 0 && tagsByMovie != null)
                {
                    List<MovieTagAssignment> movieTags;
                    if (tagsByMovie.TryGetValue(movie.Id, out movieTags))
                    {
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
