using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Compares movies side-by-side across multiple dimensions:
    /// pricing, ratings, reviews, rental popularity, and availability.
    /// </summary>
    public class MovieComparisonService
    {
        private readonly IMovieRepository _movieRepository;
        private readonly IReviewRepository _reviewRepository;
        private readonly IRentalRepository _rentalRepository;
        private readonly IClock _clock;

        public MovieComparisonService()
            : this(new InMemoryMovieRepository(),
                   new InMemoryReviewRepository(),
                   new InMemoryRentalRepository())
        {
        }

        public MovieComparisonService(
            IMovieRepository movieRepository,
            IReviewRepository reviewRepository,
            IRentalRepository rentalRepository,
            IClock clock = null)
        {
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _reviewRepository = reviewRepository
                ?? throw new ArgumentNullException(nameof(reviewRepository));
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
            _clock = clock ?? new SystemClock();
        }

        /// <summary>
        /// Build a full comparison for the given movie IDs (2-4 movies).
        /// </summary>
        public MovieComparisonResult Compare(IEnumerable<int> movieIds)
        {
            var ids = movieIds?.Distinct().ToList()
                ?? throw new ArgumentNullException(nameof(movieIds));

            if (ids.Count < 2)
                throw new ArgumentException("At least 2 movies are required for comparison.");
            if (ids.Count > 4)
                throw new ArgumentException("At most 4 movies can be compared at once.");

            var allRentals = _rentalRepository.GetAll();
            var entries = new List<MovieComparisonEntry>();

            foreach (var id in ids)
            {
                var movie = _movieRepository.GetById(id);
                if (movie == null) continue;

                var reviews = _reviewRepository.GetByMovie(id);
                var movieRentals = allRentals.Where(r => r.MovieId == id).ToList();
                var activeRentals = movieRentals.Where(r => r.Status != RentalStatus.Returned).ToList();

                var avgRating = reviews.Any()
                    ? Math.Round(reviews.Average(r => r.Stars), 1)
                    : (double?)null;

                var dailyRate = PricingService.GetMovieDailyRate(movie);

                entries.Add(new MovieComparisonEntry
                {
                    Movie = movie,
                    DailyRate = dailyRate,
                    WeeklyEstimate = dailyRate * 7,
                    AverageRating = avgRating,
                    ReviewCount = reviews.Count,
                    TotalRentals = movieRentals.Count,
                    CurrentlyRented = activeRentals.Any(),
                    IsAvailable = !activeRentals.Any(),
                    IsNewRelease = movie.IsNewRelease,
                    AgeDays = movie.ReleaseDate.HasValue
                        ? (int)(_clock.Today - movie.ReleaseDate.Value).TotalDays
                        : (int?)null
                });
            }

            if (entries.Count < 2)
                throw new ArgumentException("Could not find enough valid movies for comparison.");

            // Determine "best" in each category
            var bestRated = entries.Where(e => e.AverageRating.HasValue)
                .OrderByDescending(e => e.AverageRating).FirstOrDefault();
            var cheapest = entries.OrderBy(e => e.DailyRate).First();
            var mostPopular = entries.OrderByDescending(e => e.TotalRentals).First();
            var mostReviewed = entries.OrderByDescending(e => e.ReviewCount).First();

            return new MovieComparisonResult
            {
                Entries = entries,
                BestRatedId = bestRated?.Movie.Id,
                CheapestId = cheapest.Movie.Id,
                MostPopularId = mostPopular.Movie.Id,
                MostReviewedId = mostReviewed.Movie.Id,
                ComparedAt = _clock.Now
            };
        }

        /// <summary>
        /// Get all movies available for comparison selection.
        /// </summary>
        public IReadOnlyList<Movie> GetAvailableMovies()
        {
            return _movieRepository.GetAll();
        }
    }

    // ── Comparison Models ────────────────────────────────────────

    public class MovieComparisonEntry
    {
        public Movie Movie { get; set; }
        public decimal DailyRate { get; set; }
        public decimal WeeklyEstimate { get; set; }
        public double? AverageRating { get; set; }
        public int ReviewCount { get; set; }
        public int TotalRentals { get; set; }
        public bool CurrentlyRented { get; set; }
        public bool IsAvailable { get; set; }
        public bool IsNewRelease { get; set; }
        public int? AgeDays { get; set; }

        /// <summary>Formatted age string (e.g., "2 years", "3 months").</summary>
        public string AgeDisplay
        {
            get
            {
                if (!AgeDays.HasValue) return "Unknown";
                var days = AgeDays.Value;
                if (days < 0) return "Upcoming";
                if (days < 30) return $"{days} day{(days == 1 ? "" : "s")}";
                if (days < 365) return $"{days / 30} month{(days / 30 == 1 ? "" : "s")}";
                var years = days / 365;
                return $"{years} year{(years == 1 ? "" : "s")}";
            }
        }

        /// <summary>Star rating display (e.g., "★★★★☆").</summary>
        public string StarDisplay
        {
            get
            {
                if (!AverageRating.HasValue) return "No ratings";
                var filled = (int)Math.Round(AverageRating.Value);
                return new string('★', filled) + new string('☆', 5 - filled);
            }
        }
    }

    public class MovieComparisonResult
    {
        public List<MovieComparisonEntry> Entries { get; set; }
        public int? BestRatedId { get; set; }
        public int? CheapestId { get; set; }
        public int? MostPopularId { get; set; }
        public int? MostReviewedId { get; set; }
        public DateTime ComparedAt { get; set; }
    }
}
