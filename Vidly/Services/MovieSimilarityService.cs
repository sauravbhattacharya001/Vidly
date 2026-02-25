using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Finds similar movies using a multi-signal similarity scoring approach:
    /// genre match, rating proximity, and co-rental patterns (collaborative filtering).
    /// </summary>
    public class MovieSimilarityService
    {
        private readonly IMovieRepository _movieRepository;
        private readonly IRentalRepository _rentalRepository;

        /// <summary>Weight for genre match signal (0-1).</summary>
        public const double GenreWeight = 0.35;
        /// <summary>Weight for rating proximity signal (0-1).</summary>
        public const double RatingWeight = 0.25;
        /// <summary>Weight for co-rental pattern signal (0-1).</summary>
        public const double CoRentalWeight = 0.40;

        public MovieSimilarityService(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository)
        {
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
            _rentalRepository = rentalRepository ?? throw new ArgumentNullException(nameof(rentalRepository));
        }

        /// <summary>
        /// Find movies similar to the given movie.
        /// </summary>
        /// <param name="movieId">The source movie to find similarities for.</param>
        /// <param name="maxResults">Maximum similar movies to return (default 10).</param>
        /// <returns>A SimilarityResult with scored movie suggestions.</returns>
        public SimilarityResult FindSimilar(int movieId, int maxResults = 10)
        {
            if (maxResults < 1)
                throw new ArgumentOutOfRangeException(nameof(maxResults));

            var sourceMovie = _movieRepository.GetById(movieId);
            if (sourceMovie == null)
                throw new ArgumentException($"Movie with ID {movieId} not found.", nameof(movieId));

            var allMovies = _movieRepository.GetAll();
            var allRentals = _rentalRepository.GetAll();

            // Build co-rental index: for each movie, which other movies were rented by the same customers
            var coRentalScores = BuildCoRentalIndex(movieId, allRentals);

            // Score each candidate movie
            var candidates = new List<SimilarMovie>();
            foreach (var candidate in allMovies)
            {
                if (candidate.Id == movieId) continue;

                var genreScore = CalculateGenreScore(sourceMovie, candidate);
                var ratingScore = CalculateRatingScore(sourceMovie, candidate);
                var coRentalScore = coRentalScores.TryGetValue(candidate.Id, out var crs) ? crs : 0.0;

                var totalScore = (genreScore * GenreWeight) + (ratingScore * RatingWeight) + (coRentalScore * CoRentalWeight);

                if (totalScore > 0)
                {
                    candidates.Add(new SimilarMovie
                    {
                        Movie = candidate,
                        TotalScore = Math.Round(totalScore, 2),
                        GenreScore = Math.Round(genreScore, 2),
                        RatingScore = Math.Round(ratingScore, 2),
                        CoRentalScore = Math.Round(coRentalScore, 2),
                        Reasons = BuildReasons(sourceMovie, candidate, genreScore, ratingScore, coRentalScore)
                    });
                }
            }

            // Sort by total score descending, then by rating descending
            candidates.Sort((a, b) =>
            {
                var scoreCompare = b.TotalScore.CompareTo(a.TotalScore);
                return scoreCompare != 0 ? scoreCompare : (b.Movie.Rating ?? 0).CompareTo(a.Movie.Rating ?? 0);
            });

            var results = candidates.Take(maxResults).ToList();

            return new SimilarityResult
            {
                SourceMovie = sourceMovie,
                SimilarMovies = results,
                TotalCandidatesScored = candidates.Count,
                Signals = new SimilaritySignals
                {
                    UniqueRenters = GetUniqueRenters(movieId, allRentals),
                    CoRentedMovieCount = coRentalScores.Count,
                    HasGenre = sourceMovie.Genre.HasValue,
                    HasRating = sourceMovie.Rating.HasValue
                }
            };
        }

        /// <summary>
        /// Compare two specific movies for similarity.
        /// </summary>
        public MovieComparison Compare(int movieId1, int movieId2)
        {
            var movie1 = _movieRepository.GetById(movieId1);
            var movie2 = _movieRepository.GetById(movieId2);
            if (movie1 == null) throw new ArgumentException($"Movie {movieId1} not found.");
            if (movie2 == null) throw new ArgumentException($"Movie {movieId2} not found.");

            var allRentals = _rentalRepository.GetAll();
            var coRentalScores1 = BuildCoRentalIndex(movieId1, allRentals);

            var genreScore = CalculateGenreScore(movie1, movie2);
            var ratingScore = CalculateRatingScore(movie1, movie2);
            var coRentalScore = coRentalScores1.TryGetValue(movieId2, out var crs) ? crs : 0.0;
            var totalScore = (genreScore * GenreWeight) + (ratingScore * RatingWeight) + (coRentalScore * CoRentalWeight);

            // Shared renters
            var renters1 = new HashSet<int>(allRentals.Where(r => r.MovieId == movieId1).Select(r => r.CustomerId));
            var renters2 = new HashSet<int>(allRentals.Where(r => r.MovieId == movieId2).Select(r => r.CustomerId));
            var sharedRenters = renters1.Intersect(renters2).Count();

            return new MovieComparison
            {
                Movie1 = movie1,
                Movie2 = movie2,
                TotalScore = Math.Round(totalScore, 2),
                GenreScore = Math.Round(genreScore, 2),
                RatingScore = Math.Round(ratingScore, 2),
                CoRentalScore = Math.Round(coRentalScore, 2),
                SharedRenters = sharedRenters,
                Movie1TotalRenters = renters1.Count,
                Movie2TotalRenters = renters2.Count,
                SameGenre = movie1.Genre.HasValue && movie2.Genre.HasValue && movie1.Genre == movie2.Genre,
                RatingDifference = (movie1.Rating.HasValue && movie2.Rating.HasValue)
                    ? (int?)Math.Abs(movie1.Rating.Value - movie2.Rating.Value)
                    : null,
                Verdict = GetVerdict(totalScore)
            };
        }

        /// <summary>
        /// Get a similarity matrix for all movies (useful for analytics/heatmaps).
        /// Uses a pre-built customer-to-movies index to compute all co-rental
        /// scores in a single pass instead of scanning all rentals per movie.
        /// </summary>
        public SimilarityMatrix GetSimilarityMatrix()
        {
            var allMovies = _movieRepository.GetAll().OrderBy(m => m.Id).ToList();
            var allRentals = _rentalRepository.GetAll();
            var n = allMovies.Count;
            var scores = new double[n, n];

            // Build customer -> set of rented movie IDs index in one pass: O(R)
            var customerMovies = BuildCustomerMovieIndex(allRentals);

            // Derive co-rental indices for all movies from the customer index
            // instead of scanning allRentals M times.
            var coRentalIndices = new Dictionary<int, Dictionary<int, double>>();
            foreach (var movie in allMovies)
            {
                coRentalIndices[movie.Id] = BuildCoRentalFromIndex(movie.Id, customerMovies, allRentals);
            }

            for (int i = 0; i < n; i++)
            {
                scores[i, i] = 1.0; // Self-similarity = 1
                for (int j = i + 1; j < n; j++)
                {
                    var genreScore = CalculateGenreScore(allMovies[i], allMovies[j]);
                    var ratingScore = CalculateRatingScore(allMovies[i], allMovies[j]);
                    var coRentalScore = coRentalIndices[allMovies[i].Id]
                        .TryGetValue(allMovies[j].Id, out var crs) ? crs : 0.0;

                    var total = Math.Round(
                        (genreScore * GenreWeight) + (ratingScore * RatingWeight) + (coRentalScore * CoRentalWeight), 2);

                    scores[i, j] = total;
                    scores[j, i] = total; // Symmetric
                }
            }

            // Find clusters: movies with avg similarity > 0.5 to each other
            var clusters = FindClusters(allMovies, scores);

            return new SimilarityMatrix
            {
                Movies = allMovies,
                Scores = scores,
                Clusters = clusters
            };
        }

        // --- Scoring Methods ---

        internal static double CalculateGenreScore(Movie source, Movie candidate)
        {
            if (!source.Genre.HasValue || !candidate.Genre.HasValue) return 0.0;
            return source.Genre.Value == candidate.Genre.Value ? 1.0 : 0.0;
        }

        internal static double CalculateRatingScore(Movie source, Movie candidate)
        {
            if (!source.Rating.HasValue || !candidate.Rating.HasValue) return 0.0;
            var diff = Math.Abs(source.Rating.Value - candidate.Rating.Value);
            // Rating 1-5, max diff = 4. Score: 1.0 (same) -> 0.0 (diff=4)
            return 1.0 - (diff / 4.0);
        }

        internal static Dictionary<int, double> BuildCoRentalIndex(
            int movieId, IReadOnlyList<Rental> allRentals)
        {
            // Find all customers who rented this movie
            var renters = new HashSet<int>(
                allRentals.Where(r => r.MovieId == movieId).Select(r => r.CustomerId));

            if (renters.Count == 0) return new Dictionary<int, double>();

            // For each renter, find what other movies they rented
            var coRentalCounts = new Dictionary<int, int>();
            foreach (var rental in allRentals)
            {
                if (rental.MovieId == movieId) continue;
                if (!renters.Contains(rental.CustomerId)) continue;

                if (!coRentalCounts.ContainsKey(rental.MovieId))
                    coRentalCounts[rental.MovieId] = 0;
                coRentalCounts[rental.MovieId]++;
            }

            if (coRentalCounts.Count == 0) return new Dictionary<int, double>();

            // Normalize: divide by max count to get 0-1 score
            var maxCount = coRentalCounts.Values.Max();
            return coRentalCounts.ToDictionary(
                kvp => kvp.Key,
                kvp => (double)kvp.Value / maxCount);
        }

        private static int GetUniqueRenters(int movieId, IReadOnlyList<Rental> allRentals)
        {
            return allRentals.Where(r => r.MovieId == movieId)
                .Select(r => r.CustomerId)
                .Distinct()
                .Count();
        }

        /// <summary>
        /// Build an index mapping each customer to the set of movies they rented.
        /// Single O(R) pass over all rentals.
        /// </summary>
        internal static Dictionary<int, HashSet<int>> BuildCustomerMovieIndex(
            IReadOnlyList<Rental> allRentals)
        {
            var index = new Dictionary<int, HashSet<int>>();
            foreach (var r in allRentals)
            {
                if (!index.TryGetValue(r.CustomerId, out var movies))
                {
                    movies = new HashSet<int>();
                    index[r.CustomerId] = movies;
                }
                movies.Add(r.MovieId);
            }
            return index;
        }

        /// <summary>
        /// Build co-rental scores for a movie using a pre-built customer-movie index.
        /// Avoids scanning all rentals again — only iterates renters of this movie
        /// and their rented movies.
        /// </summary>
        internal static Dictionary<int, double> BuildCoRentalFromIndex(
            int movieId,
            Dictionary<int, HashSet<int>> customerMovies,
            IReadOnlyList<Rental> allRentals)
        {
            // Find renters of this movie
            var renters = new HashSet<int>();
            foreach (var r in allRentals)
            {
                if (r.MovieId == movieId)
                    renters.Add(r.CustomerId);
            }

            if (renters.Count == 0) return new Dictionary<int, double>();

            // Count co-rentals using the customer index
            var coRentalCounts = new Dictionary<int, int>();
            foreach (var customerId in renters)
            {
                if (!customerMovies.TryGetValue(customerId, out var theirMovies)) continue;
                foreach (var mid in theirMovies)
                {
                    if (mid == movieId) continue;
                    if (!coRentalCounts.ContainsKey(mid))
                        coRentalCounts[mid] = 0;
                    coRentalCounts[mid]++;
                }
            }

            if (coRentalCounts.Count == 0) return new Dictionary<int, double>();

            var maxCount = coRentalCounts.Values.Max();
            return coRentalCounts.ToDictionary(
                kvp => kvp.Key,
                kvp => (double)kvp.Value / maxCount);
        }

        internal static List<string> BuildReasons(Movie source, Movie candidate,
            double genreScore, double ratingScore, double coRentalScore)
        {
            var reasons = new List<string>();

            if (genreScore > 0)
                reasons.Add($"Same genre ({source.Genre})");

            if (ratingScore >= 0.75)
                reasons.Add("Very similar rating");
            else if (ratingScore >= 0.5)
                reasons.Add("Similar rating");

            if (coRentalScore >= 0.7)
                reasons.Add("Frequently rented together");
            else if (coRentalScore >= 0.3)
                reasons.Add("Often rented by same customers");
            else if (coRentalScore > 0)
                reasons.Add("Some rental overlap");

            return reasons;
        }

        internal static string GetVerdict(double score)
        {
            if (score >= 0.8) return "Highly Similar";
            if (score >= 0.6) return "Very Similar";
            if (score >= 0.4) return "Moderately Similar";
            if (score >= 0.2) return "Somewhat Similar";
            if (score > 0) return "Slightly Similar";
            return "Not Similar";
        }

        private static List<MovieCluster> FindClusters(List<Movie> movies, double[,] scores)
        {
            var n = movies.Count;
            var visited = new bool[n];
            var clusters = new List<MovieCluster>();
            const double clusterThreshold = 0.4;

            for (int i = 0; i < n; i++)
            {
                if (visited[i]) continue;

                var clusterMembers = new List<int> { i };
                visited[i] = true;

                for (int j = i + 1; j < n; j++)
                {
                    if (visited[j]) continue;

                    // Check if j is similar enough to ALL current cluster members
                    bool fitsCluster = clusterMembers.All(m => scores[m, j] >= clusterThreshold);
                    if (fitsCluster)
                    {
                        clusterMembers.Add(j);
                        visited[j] = true;
                    }
                }

                if (clusterMembers.Count >= 2)
                {
                    var clusterMovies = clusterMembers.Select(idx => movies[idx]).ToList();
                    var avgScore = 0.0;
                    var pairs = 0;
                    for (int a = 0; a < clusterMembers.Count; a++)
                    {
                        for (int b = a + 1; b < clusterMembers.Count; b++)
                        {
                            avgScore += scores[clusterMembers[a], clusterMembers[b]];
                            pairs++;
                        }
                    }
                    avgScore = pairs > 0 ? Math.Round(avgScore / pairs, 2) : 0;

                    // Determine dominant genre
                    var dominantGenre = clusterMovies
                        .Where(m => m.Genre.HasValue)
                        .GroupBy(m => m.Genre.Value)
                        .OrderByDescending(g => g.Count())
                        .Select(g => g.Key.ToString())
                        .FirstOrDefault() ?? "Mixed";

                    clusters.Add(new MovieCluster
                    {
                        Movies = clusterMovies,
                        AverageSimilarity = avgScore,
                        DominantGenre = dominantGenre
                    });
                }
            }

            return clusters;
        }
    }

    // --- Result Models ---

    public class SimilarityResult
    {
        public Movie SourceMovie { get; set; }
        public List<SimilarMovie> SimilarMovies { get; set; } = new List<SimilarMovie>();
        public int TotalCandidatesScored { get; set; }
        public SimilaritySignals Signals { get; set; }
    }

    public class SimilarMovie
    {
        public Movie Movie { get; set; }
        public double TotalScore { get; set; }
        public double GenreScore { get; set; }
        public double RatingScore { get; set; }
        public double CoRentalScore { get; set; }
        public List<string> Reasons { get; set; } = new List<string>();
    }

    public class SimilaritySignals
    {
        public int UniqueRenters { get; set; }
        public int CoRentedMovieCount { get; set; }
        public bool HasGenre { get; set; }
        public bool HasRating { get; set; }
    }

    public class MovieComparison
    {
        public Movie Movie1 { get; set; }
        public Movie Movie2 { get; set; }
        public double TotalScore { get; set; }
        public double GenreScore { get; set; }
        public double RatingScore { get; set; }
        public double CoRentalScore { get; set; }
        public int SharedRenters { get; set; }
        public int Movie1TotalRenters { get; set; }
        public int Movie2TotalRenters { get; set; }
        public bool SameGenre { get; set; }
        public int? RatingDifference { get; set; }
        public string Verdict { get; set; }
    }

    public class SimilarityMatrix
    {
        public List<Movie> Movies { get; set; }
        public double[,] Scores { get; set; }
        public List<MovieCluster> Clusters { get; set; } = new List<MovieCluster>();
    }

    public class MovieCluster
    {
        public List<Movie> Movies { get; set; } = new List<Movie>();
        public double AverageSimilarity { get; set; }
        public string DominantGenre { get; set; }
    }
}
