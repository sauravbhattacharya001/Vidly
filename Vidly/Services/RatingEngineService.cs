using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Advanced movie rating engine with Bayesian weighted ratings,
    /// trending scores, genre rankings, and controversy detection.
    ///
    /// Goes beyond simple averages to produce fair, robust rankings
    /// that account for review count, recency, and rating variance.
    /// </summary>
    public class RatingEngineService
    {
        private readonly IReviewRepository _reviewRepo;
        private readonly IMovieRepository _movieRepo;
        private readonly IClock _clock;

        /// <summary>
        /// Minimum number of reviews for a movie to qualify for ranked lists.
        /// Movies below this threshold get a Bayesian prior pull toward the global mean.
        /// </summary>
        public const int DefaultMinVotes = 3;

        /// <summary>
        /// Half-life in days for trending score exponential decay.
        /// Reviews older than this contribute ~50% of a recent review's weight.
        /// </summary>
        public const double TrendingHalfLifeDays = 30.0;

        public RatingEngineService(
            IReviewRepository reviewRepo,
            IMovieRepository movieRepo,
            IClock clock = null)
        {
            _reviewRepo = reviewRepo
                ?? throw new ArgumentNullException(nameof(reviewRepo));
            _movieRepo = movieRepo
                ?? throw new ArgumentNullException(nameof(movieRepo));
            _clock = clock ?? new SystemClock();
        }

        /// <summary>
        /// Compute Bayesian weighted rating for a movie (IMDb WR formula).
        ///
        /// WR = (v / (v + m)) * R + (m / (v + m)) * C
        ///
        /// Where:
        ///   R = movie's arithmetic mean rating
        ///   v = number of votes for this movie
        ///   m = minimum votes required (prior strength)
        ///   C = global mean rating across all movies
        /// </summary>
        public BayesianRating GetBayesianRating(int movieId, int minVotes = DefaultMinVotes)
        {
            if (minVotes < 1)
                throw new ArgumentOutOfRangeException(nameof(minVotes), "Minimum votes must be at least 1.");

            var movie = _movieRepo.GetById(movieId);
            if (movie == null) return null;

            var movieReviews = _reviewRepo.GetByMovie(movieId);
            var allReviews = _reviewRepo.GetAll();

            double globalMean = allReviews.Count > 0
                ? allReviews.Average(r => r.Stars)
                : 3.0;

            double movieMean = movieReviews.Count > 0
                ? movieReviews.Average(r => r.Stars)
                : 0;

            int voteCount = movieReviews.Count;

            double weighted = voteCount > 0
                ? ((double)voteCount / (voteCount + minVotes)) * movieMean
                  + ((double)minVotes / (voteCount + minVotes)) * globalMean
                : globalMean;

            return new BayesianRating
            {
                MovieId = movieId,
                MovieName = movie.Name,
                Genre = movie.Genre,
                ArithmeticMean = Math.Round(movieMean, 2),
                BayesianWeightedRating = Math.Round(weighted, 2),
                VoteCount = voteCount,
                MinVotesThreshold = minVotes,
                GlobalMean = Math.Round(globalMean, 2),
                MeetsThreshold = voteCount >= minVotes
            };
        }

        /// <summary>
        /// Rank all movies by Bayesian weighted rating (descending).
        /// </summary>
        public IReadOnlyList<BayesianRating> GetRankedMovies(
            int minVotes = DefaultMinVotes,
            int? limit = null)
        {
            var movies = _movieRepo.GetAll();
            var allReviews = _reviewRepo.GetAll();

            double globalMean = allReviews.Count > 0
                ? allReviews.Average(r => r.Stars)
                : 3.0;

            var reviewsByMovie = allReviews
                .GroupBy(r => r.MovieId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var ratings = new List<BayesianRating>();

            foreach (var movie in movies)
            {
                List<Review> reviews;
                reviewsByMovie.TryGetValue(movie.Id, out reviews);
                int v = reviews != null ? reviews.Count : 0;
                double R = v > 0 ? reviews.Average(r => r.Stars) : 0;

                double wr = v > 0
                    ? ((double)v / (v + minVotes)) * R
                      + ((double)minVotes / (v + minVotes)) * globalMean
                    : globalMean;

                ratings.Add(new BayesianRating
                {
                    MovieId = movie.Id,
                    MovieName = movie.Name,
                    Genre = movie.Genre,
                    ArithmeticMean = Math.Round(R, 2),
                    BayesianWeightedRating = Math.Round(wr, 2),
                    VoteCount = v,
                    MinVotesThreshold = minVotes,
                    GlobalMean = Math.Round(globalMean, 2),
                    MeetsThreshold = v >= minVotes
                });
            }

            var sorted = ratings
                .OrderByDescending(r => r.BayesianWeightedRating)
                .ThenByDescending(r => r.VoteCount)
                .ToList();

            if (limit.HasValue && limit.Value > 0)
                return sorted.Take(limit.Value).ToList();

            return sorted;
        }

        /// <summary>
        /// Get top-rated movies for a specific genre.
        /// </summary>
        public IReadOnlyList<BayesianRating> GetGenreRankings(
            Genre genre,
            int minVotes = DefaultMinVotes,
            int? limit = null)
        {
            var all = GetRankedMovies(minVotes);
            var filtered = all.Where(r => r.Genre == genre).ToList();

            if (limit.HasValue && limit.Value > 0)
                return filtered.Take(limit.Value).ToList();

            return filtered;
        }

        /// <summary>
        /// Get rankings for every genre as a dictionary.
        /// </summary>
        public IDictionary<Genre, IReadOnlyList<BayesianRating>> GetAllGenreRankings(
            int minVotes = DefaultMinVotes,
            int topPerGenre = 10)
        {
            var all = GetRankedMovies(minVotes);
            var result = new Dictionary<Genre, IReadOnlyList<BayesianRating>>();

            foreach (Genre genre in Enum.GetValues(typeof(Genre)))
            {
                var genreMovies = all
                    .Where(r => r.Genre == genre)
                    .Take(topPerGenre)
                    .ToList();
                if (genreMovies.Count > 0)
                    result[genre] = genreMovies;
            }

            return result;
        }

        /// <summary>
        /// Compute a trending score that weights recent reviews more heavily
        /// using exponential decay. Movies with many recent positive reviews
        /// score higher than movies whose reviews are all old.
        /// </summary>
        public TrendingScore GetTrendingScore(int movieId, DateTime? asOf = null)
        {
            var movie = _movieRepo.GetById(movieId);
            if (movie == null) return null;

            var reviews = _reviewRepo.GetByMovie(movieId);
            var now = asOf ?? _clock.Now;
            var lambda = Math.Log(2) / TrendingHalfLifeDays;

            double weightedSum = 0;
            double weightTotal = 0;
            int recentCount = 0;

            foreach (var review in reviews)
            {
                double ageDays = (now - review.CreatedDate).TotalDays;
                if (ageDays < 0) ageDays = 0;
                double weight = Math.Exp(-lambda * ageDays);

                weightedSum += review.Stars * weight;
                weightTotal += weight;

                if (ageDays <= 30) recentCount++;
            }

            double trendingRating = weightTotal > 0
                ? weightedSum / weightTotal
                : 0;

            double score = trendingRating * Math.Log(1 + recentCount);

            return new TrendingScore
            {
                MovieId = movieId,
                MovieName = movie.Name,
                TrendingRating = Math.Round(trendingRating, 2),
                Score = Math.Round(score, 2),
                TotalReviews = reviews.Count,
                RecentReviews = recentCount,
                HalfLifeDays = TrendingHalfLifeDays
            };
        }

        /// <summary>
        /// Get top trending movies across the catalog.
        /// </summary>
        public IReadOnlyList<TrendingScore> GetTrendingMovies(
            int count = 10,
            DateTime? asOf = null)
        {
            if (count < 1)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be at least 1.");

            var movies = _movieRepo.GetAll();
            var scores = new List<TrendingScore>();

            foreach (var movie in movies)
            {
                var ts = GetTrendingScore(movie.Id, asOf);
                if (ts != null && ts.TotalReviews > 0)
                    scores.Add(ts);
            }

            return scores
                .OrderByDescending(s => s.Score)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// Compute the controversy score for a movie based on rating variance.
        /// High variance (e.g., lots of 1s and 5s) indicates a controversial film.
        ///
        /// Uses standard deviation normalized against the theoretical maximum
        /// and polarization (ratio of extreme 1-star and 5-star ratings).
        /// </summary>
        public ControversyScore GetControversyScore(int movieId)
        {
            var movie = _movieRepo.GetById(movieId);
            if (movie == null) return null;

            var reviews = _reviewRepo.GetByMovie(movieId);
            if (reviews.Count < 2)
            {
                return new ControversyScore
                {
                    MovieId = movieId,
                    MovieName = movie.Name,
                    Score = 0,
                    Label = "Insufficient Data",
                    ReviewCount = reviews.Count,
                    StandardDeviation = 0,
                    Distribution = BuildDistribution(reviews)
                };
            }

            var stars = reviews.Select(r => (double)r.Stars).ToList();
            double mean = stars.Average();
            double variance = stars.Sum(s => (s - mean) * (s - mean)) / (stars.Count - 1);
            double stdDev = Math.Sqrt(variance);

            double normalizedStdDev = stdDev / 2.0;

            int extremeCount = reviews.Count(r => r.Stars == 1 || r.Stars == 5);
            double polarization = (double)extremeCount / reviews.Count;

            double score = (0.6 * normalizedStdDev + 0.4 * polarization) * 100;
            score = Math.Min(100, Math.Round(score, 1));

            string label;
            if (score >= 70) label = "Highly Controversial";
            else if (score >= 50) label = "Controversial";
            else if (score >= 30) label = "Mildly Divisive";
            else label = "Consensus";

            return new ControversyScore
            {
                MovieId = movieId,
                MovieName = movie.Name,
                Score = score,
                Label = label,
                ReviewCount = reviews.Count,
                StandardDeviation = Math.Round(stdDev, 2),
                Polarization = Math.Round(polarization * 100, 1),
                MeanRating = Math.Round(mean, 2),
                Distribution = BuildDistribution(reviews)
            };
        }

        /// <summary>
        /// Get the most controversial movies in the catalog.
        /// </summary>
        public IReadOnlyList<ControversyScore> GetMostControversial(
            int count = 10,
            int minReviews = 5)
        {
            if (count < 1)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be at least 1.");

            var movies = _movieRepo.GetAll();
            var scores = new List<ControversyScore>();

            foreach (var movie in movies)
            {
                var cs = GetControversyScore(movie.Id);
                if (cs != null && cs.ReviewCount >= minReviews)
                    scores.Add(cs);
            }

            return scores
                .OrderByDescending(s => s.Score)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// Comprehensive rating report for a movie combining all metrics.
        /// </summary>
        public MovieRatingReport GetRatingReport(int movieId, int minVotes = DefaultMinVotes)
        {
            var bayesian = GetBayesianRating(movieId, minVotes);
            if (bayesian == null) return null;

            var trending = GetTrendingScore(movieId);
            var controversy = GetControversyScore(movieId);

            return new MovieRatingReport
            {
                Bayesian = bayesian,
                Trending = trending,
                Controversy = controversy
            };
        }

        private int[] BuildDistribution(IReadOnlyList<Review> reviews)
        {
            var dist = new int[5];
            foreach (var r in reviews)
            {
                if (r.Stars >= 1 && r.Stars <= 5)
                    dist[r.Stars - 1]++;
            }
            return dist;
        }
    }

    // -- Data Transfer Objects --

    public class BayesianRating
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public Genre? Genre { get; set; }
        public double ArithmeticMean { get; set; }
        public double BayesianWeightedRating { get; set; }
        public int VoteCount { get; set; }
        public int MinVotesThreshold { get; set; }
        public double GlobalMean { get; set; }
        public bool MeetsThreshold { get; set; }
    }

    public class TrendingScore
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public double TrendingRating { get; set; }
        public double Score { get; set; }
        public int TotalReviews { get; set; }
        public int RecentReviews { get; set; }
        public double HalfLifeDays { get; set; }
    }

    public class ControversyScore
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public double Score { get; set; }
        public string Label { get; set; }
        public int ReviewCount { get; set; }
        public double StandardDeviation { get; set; }
        public double Polarization { get; set; }
        public double MeanRating { get; set; }
        public int[] Distribution { get; set; }
    }

    public class MovieRatingReport
    {
        public BayesianRating Bayesian { get; set; }
        public TrendingScore Trending { get; set; }
        public ControversyScore Controversy { get; set; }
    }
}
