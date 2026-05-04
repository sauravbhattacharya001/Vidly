using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    // ================================================================
    //  Cultural Moment Detector — autonomous detection of movie
    //  cultural relevance events with proactive recommendations.
    //
    //  7 engines:
    //  1. Anniversary Detector — milestone anniversaries (5/10/15/20/25/30/40/50 yr)
    //  2. Franchise Surge Detector — correlated franchise rental spikes
    //  3. Genre Momentum Detector — genre trending above baseline
    //  4. Nostalgia Cycle Detector — 20/30-year cycle resurgence
    //  5. Spotlight Detector — creator-correlated spikes (name-prefix proxy)
    //  6. Dormant Revival Detector — suddenly-rented dormant titles
    //  7. Insight Generator — natural-language insights
    // ================================================================

    #region Models

    public class CulturalMoment
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public Genre? Genre { get; set; }
        public string MomentType { get; set; }
        public string Description { get; set; }
        public double RelevanceScore { get; set; }
        public DateTime DetectedAt { get; set; }
        public string RecommendedAction { get; set; }
        public int Priority { get; set; }
    }

    public class CulturalMomentReport
    {
        public DateTime GeneratedAt { get; set; }
        public List<CulturalMoment> Moments { get; set; }
        public List<GenreMomentumEntry> GenreMomentum { get; set; }
        public List<string> Insights { get; set; }
        public double CulturalPulseScore { get; set; }
        public int TotalMomentsDetected { get; set; }
        public Dictionary<string, int> MomentsByType { get; set; }
    }

    public class GenreMomentumEntry
    {
        public Genre Genre { get; set; }
        public double RecentVelocity { get; set; }
        public double HistoricalBaseline { get; set; }
        public double MomentumRatio { get; set; }
        public string Trend { get; set; }
    }

    public class CulturalMomentConfig
    {
        public int AnniversaryWindowDays { get; set; }
        public int FranchiseSpikeThreshold { get; set; }
        public double GenreMomentumThreshold { get; set; }
        public int DormantDaysThreshold { get; set; }
        public int RecentWindowDays { get; set; }
        public int HistoricalWindowDays { get; set; }
        public int NostalgiaCycleYears { get; set; }

        public CulturalMomentConfig()
        {
            AnniversaryWindowDays = 30;
            FranchiseSpikeThreshold = 3;
            GenreMomentumThreshold = 1.5;
            DormantDaysThreshold = 60;
            RecentWindowDays = 30;
            HistoricalWindowDays = 180;
            NostalgiaCycleYears = 20;
        }
    }

    #endregion

    public class CulturalMomentService
    {
        private readonly IRentalRepository _rentalRepo;
        private readonly IMovieRepository _movieRepo;
        private readonly IClock _clock;
        private readonly CulturalMomentConfig _config;

        private static readonly int[] MilestoneYears = { 5, 10, 15, 20, 25, 30, 40, 50 };

        public CulturalMomentService(
            IRentalRepository rentalRepo,
            IMovieRepository movieRepo,
            IClock clock,
            CulturalMomentConfig config = null)
        {
            if (rentalRepo == null) throw new ArgumentNullException("rentalRepo");
            if (movieRepo == null) throw new ArgumentNullException("movieRepo");
            if (clock == null) throw new ArgumentNullException("clock");
            _rentalRepo = rentalRepo;
            _movieRepo = movieRepo;
            _clock = clock;
            _config = config ?? new CulturalMomentConfig();
        }

        // ----------------------------------------------------------------
        //  Public API
        // ----------------------------------------------------------------

        public CulturalMomentReport Analyze()
        {
            var now = _clock.Now;
            var movies = _movieRepo.GetAll();
            var rentals = _rentalRepo.GetAll();
            var movieLookup = movies.ToDictionary(m => m.Id, m => m);

            var moments = new List<CulturalMoment>();

            moments.AddRange(DetectAnniversaries(movies, now));
            moments.AddRange(DetectFranchiseSurges(movies, rentals, movieLookup, now));
            moments.AddRange(DetectNostalgiaCycles(movies, rentals, movieLookup, now));
            moments.AddRange(DetectSpotlights(movies, rentals, movieLookup, now));
            moments.AddRange(DetectDormantRevivals(movies, rentals, now));

            var genreMomentum = ComputeGenreMomentum(rentals, movieLookup, now);
            moments.AddRange(GenreMomentumToMoments(genreMomentum, now));

            // Deduplicate by MovieId+MomentType
            moments = moments
                .GroupBy(m => new { m.MovieId, m.MomentType })
                .Select(g => g.OrderByDescending(x => x.RelevanceScore).First())
                .OrderByDescending(m => m.RelevanceScore)
                .ToList();

            var insights = GenerateInsights(moments, genreMomentum, now);
            var score = ComputeHealthScore(moments, genreMomentum);

            var byType = moments
                .GroupBy(m => m.MomentType)
                .ToDictionary(g => g.Key, g => g.Count());

            return new CulturalMomentReport
            {
                GeneratedAt = now,
                Moments = moments,
                GenreMomentum = genreMomentum,
                Insights = insights,
                CulturalPulseScore = score,
                TotalMomentsDetected = moments.Count,
                MomentsByType = byType
            };
        }

        public List<CulturalMoment> GetTopMoments(int count = 10)
        {
            return Analyze().Moments.Take(count).ToList();
        }

        public List<GenreMomentumEntry> GetGenreMomentum()
        {
            var rentals = _rentalRepo.GetAll();
            var movies = _movieRepo.GetAll();
            var movieLookup = movies.ToDictionary(m => m.Id, m => m);
            return ComputeGenreMomentum(rentals, movieLookup, _clock.Now);
        }

        public List<CulturalMoment> GetMomentsByType(string momentType)
        {
            if (string.IsNullOrWhiteSpace(momentType))
                return new List<CulturalMoment>();
            return Analyze().Moments
                .Where(m => m.MomentType.Equals(momentType, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // ----------------------------------------------------------------
        //  Engine 1: Anniversary Detector
        // ----------------------------------------------------------------

        private List<CulturalMoment> DetectAnniversaries(IReadOnlyList<Movie> movies, DateTime now)
        {
            var results = new List<CulturalMoment>();
            foreach (var movie in movies)
            {
                if (!movie.ReleaseDate.HasValue) continue;
                var age = now.Year - movie.ReleaseDate.Value.Year;
                // Adjust if birthday hasn't occurred yet this year
                var anniversaryThisYear = movie.ReleaseDate.Value.AddYears(age);
                if (anniversaryThisYear > now) age--;

                foreach (var milestone in MilestoneYears)
                {
                    var anniversaryDate = movie.ReleaseDate.Value.AddYears(milestone);
                    var daysUntil = (anniversaryDate - now).TotalDays;
                    if (daysUntil >= 0 && daysUntil <= _config.AnniversaryWindowDays)
                    {
                        var urgency = 1.0 - (daysUntil / _config.AnniversaryWindowDays);
                        var relevance = 50 + (milestone * 1.0) + (urgency * 20);
                        if (relevance > 100) relevance = 100;

                        results.Add(new CulturalMoment
                        {
                            MovieId = movie.Id,
                            MovieName = movie.Name,
                            Genre = movie.Genre,
                            MomentType = "Anniversary",
                            Description = string.Format("{0}-year anniversary of \"{1}\" in {2} days",
                                milestone, movie.Name, (int)daysUntil),
                            RelevanceScore = Math.Round(relevance, 1),
                            DetectedAt = now,
                            RecommendedAction = milestone >= 25 ? "Feature" : "Promote",
                            Priority = milestone >= 25 ? 1 : 2
                        });
                        break; // Only report nearest milestone
                    }
                }
            }
            return results;
        }

        // ----------------------------------------------------------------
        //  Engine 2: Franchise Surge Detector
        // ----------------------------------------------------------------

        private List<CulturalMoment> DetectFranchiseSurges(
            IReadOnlyList<Movie> movies,
            IReadOnlyList<Rental> rentals,
            Dictionary<int, Movie> movieLookup,
            DateTime now)
        {
            var results = new List<CulturalMoment>();
            var recentStart = now.AddDays(-_config.RecentWindowDays);

            // Group movies by first word as franchise proxy
            var franchiseGroups = movies
                .Where(m => !string.IsNullOrWhiteSpace(m.Name))
                .GroupBy(m => m.Name.Split(new[] { ' ', ':', '-' }, StringSplitOptions.RemoveEmptyEntries).First().ToLowerInvariant())
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var franchise in franchiseGroups)
            {
                var franchiseIds = new HashSet<int>(franchise.Select(m => m.Id));
                var recentRentals = rentals
                    .Where(r => franchiseIds.Contains(r.MovieId) && r.RentalDate >= recentStart)
                    .ToList();

                if (recentRentals.Count >= _config.FranchiseSpikeThreshold)
                {
                    // Find movies in franchise that weren't rented recently — recommend them
                    var rentedIds = new HashSet<int>(recentRentals.Select(r => r.MovieId));
                    var unrentedMovies = franchise.Where(m => !rentedIds.Contains(m.Id)).ToList();

                    foreach (var movie in unrentedMovies)
                    {
                        results.Add(new CulturalMoment
                        {
                            MovieId = movie.Id,
                            MovieName = movie.Name,
                            Genre = movie.Genre,
                            MomentType = "FranchiseSurge",
                            Description = string.Format("Franchise \"{0}\" has {1} recent rentals — \"{2}\" may see spillover demand",
                                franchise.Key, recentRentals.Count, movie.Name),
                            RelevanceScore = Math.Min(100, 50 + recentRentals.Count * 10),
                            DetectedAt = now,
                            RecommendedAction = "Restock",
                            Priority = 2
                        });
                    }

                    // Also flag the hot ones
                    foreach (var movie in franchise.Where(m => rentedIds.Contains(m.Id)))
                    {
                        var count = recentRentals.Count(r => r.MovieId == movie.Id);
                        results.Add(new CulturalMoment
                        {
                            MovieId = movie.Id,
                            MovieName = movie.Name,
                            Genre = movie.Genre,
                            MomentType = "FranchiseSurge",
                            Description = string.Format("\"{0}\" is driving franchise \"{1}\" surge with {2} rentals",
                                movie.Name, franchise.Key, count),
                            RelevanceScore = Math.Min(100, 60 + count * 10),
                            DetectedAt = now,
                            RecommendedAction = "Promote",
                            Priority = 2
                        });
                    }
                }
            }
            return results;
        }

        // ----------------------------------------------------------------
        //  Engine 3: Genre Momentum Detector
        // ----------------------------------------------------------------

        private List<GenreMomentumEntry> ComputeGenreMomentum(
            IReadOnlyList<Rental> rentals,
            Dictionary<int, Movie> movieLookup,
            DateTime now)
        {
            var recentStart = now.AddDays(-_config.RecentWindowDays);
            var historicalStart = now.AddDays(-_config.HistoricalWindowDays);

            var results = new List<GenreMomentumEntry>();

            foreach (Genre genre in Enum.GetValues(typeof(Genre)))
            {
                var genreMovieIds = new HashSet<int>(
                    movieLookup.Values.Where(m => m.Genre == genre).Select(m => m.Id));

                if (genreMovieIds.Count == 0) continue;

                var recentCount = rentals.Count(r =>
                    genreMovieIds.Contains(r.MovieId) && r.RentalDate >= recentStart);
                var historicalCount = rentals.Count(r =>
                    genreMovieIds.Contains(r.MovieId) && r.RentalDate >= historicalStart && r.RentalDate < recentStart);

                var recentVelocity = (double)recentCount / _config.RecentWindowDays;
                var historicalDays = (_config.HistoricalWindowDays - _config.RecentWindowDays);
                var historicalBaseline = historicalDays > 0
                    ? (double)historicalCount / historicalDays
                    : 0;

                var ratio = historicalBaseline > 0
                    ? recentVelocity / historicalBaseline
                    : (recentVelocity > 0 ? 10.0 : 1.0);

                string trend;
                if (ratio >= 2.0) trend = "Surging";
                else if (ratio >= _config.GenreMomentumThreshold) trend = "Rising";
                else if (ratio >= 0.7) trend = "Stable";
                else trend = "Declining";

                results.Add(new GenreMomentumEntry
                {
                    Genre = genre,
                    RecentVelocity = Math.Round(recentVelocity, 3),
                    HistoricalBaseline = Math.Round(historicalBaseline, 3),
                    MomentumRatio = Math.Round(ratio, 2),
                    Trend = trend
                });
            }

            return results.OrderByDescending(e => e.MomentumRatio).ToList();
        }

        private List<CulturalMoment> GenreMomentumToMoments(List<GenreMomentumEntry> momentum, DateTime now)
        {
            var results = new List<CulturalMoment>();
            foreach (var entry in momentum.Where(e => e.Trend == "Surging" || e.Trend == "Rising"))
            {
                results.Add(new CulturalMoment
                {
                    MovieId = 0,
                    MovieName = string.Format("[Genre: {0}]", entry.Genre),
                    Genre = entry.Genre,
                    MomentType = "GenreMomentum",
                    Description = string.Format("{0} genre is {1} — {2:F1}x above baseline",
                        entry.Genre, entry.Trend.ToLowerInvariant(), entry.MomentumRatio),
                    RelevanceScore = Math.Min(100, entry.MomentumRatio * 30),
                    DetectedAt = now,
                    RecommendedAction = entry.Trend == "Surging" ? "Restock" : "Promote",
                    Priority = entry.Trend == "Surging" ? 1 : 3
                });
            }
            return results;
        }

        // ----------------------------------------------------------------
        //  Engine 4: Nostalgia Cycle Detector
        // ----------------------------------------------------------------

        private List<CulturalMoment> DetectNostalgiaCycles(
            IReadOnlyList<Movie> movies,
            IReadOnlyList<Rental> rentals,
            Dictionary<int, Movie> movieLookup,
            DateTime now)
        {
            var results = new List<CulturalMoment>();
            var recentStart = now.AddDays(-_config.RecentWindowDays);

            // Movies in the nostalgia sweet spot (18-22 years old or 28-32 years old)
            var nostalgiaMovies = movies.Where(m =>
            {
                if (!m.ReleaseDate.HasValue) return false;
                var age = (now - m.ReleaseDate.Value).TotalDays / 365.25;
                return (age >= _config.NostalgiaCycleYears - 2 && age <= _config.NostalgiaCycleYears + 2) ||
                       (age >= 28 && age <= 32);
            }).ToList();

            foreach (var movie in nostalgiaMovies)
            {
                var recentRentals = rentals.Count(r =>
                    r.MovieId == movie.Id && r.RentalDate >= recentStart);

                if (recentRentals > 0)
                {
                    var age = (int)((now - movie.ReleaseDate.Value).TotalDays / 365.25);
                    results.Add(new CulturalMoment
                    {
                        MovieId = movie.Id,
                        MovieName = movie.Name,
                        Genre = movie.Genre,
                        MomentType = "NostalgiaCycle",
                        Description = string.Format("\"{0}\" ({1} years old) is in nostalgia sweet spot with {2} recent rental(s)",
                            movie.Name, age, recentRentals),
                        RelevanceScore = Math.Min(100, 40 + recentRentals * 20),
                        DetectedAt = now,
                        RecommendedAction = "Feature",
                        Priority = 3
                    });
                }
            }
            return results;
        }

        // ----------------------------------------------------------------
        //  Engine 5: Spotlight Detector (creator proxy via name prefix)
        // ----------------------------------------------------------------

        private List<CulturalMoment> DetectSpotlights(
            IReadOnlyList<Movie> movies,
            IReadOnlyList<Rental> rentals,
            Dictionary<int, Movie> movieLookup,
            DateTime now)
        {
            var results = new List<CulturalMoment>();
            var recentStart = now.AddDays(-_config.RecentWindowDays);

            // Group by second word (if exists) as "creator" proxy to avoid overlap with franchise detector
            var creatorGroups = movies
                .Where(m => !string.IsNullOrWhiteSpace(m.Name))
                .Select(m => new
                {
                    Movie = m,
                    Words = m.Name.Split(new[] { ' ', ':', '-' }, StringSplitOptions.RemoveEmptyEntries)
                })
                .Where(x => x.Words.Length >= 2)
                .GroupBy(x => x.Words[1].ToLowerInvariant())
                .Where(g => g.Count() >= 2)
                .ToList();

            foreach (var group in creatorGroups)
            {
                var groupIds = new HashSet<int>(group.Select(x => x.Movie.Id));
                var recentRentals = rentals
                    .Where(r => groupIds.Contains(r.MovieId) && r.RentalDate >= recentStart)
                    .ToList();

                // Need rentals spread across multiple movies in the group
                var rentedMovies = recentRentals.Select(r => r.MovieId).Distinct().Count();
                if (rentedMovies >= 2 && recentRentals.Count >= 3)
                {
                    foreach (var item in group)
                    {
                        var movieRentals = recentRentals.Count(r => r.MovieId == item.Movie.Id);
                        if (movieRentals == 0)
                        {
                            results.Add(new CulturalMoment
                            {
                                MovieId = item.Movie.Id,
                                MovieName = item.Movie.Name,
                                Genre = item.Movie.Genre,
                                MomentType = "Spotlight",
                                Description = string.Format("Creator spotlight: related titles trending — \"{0}\" may benefit",
                                    item.Movie.Name),
                                RelevanceScore = Math.Min(100, 45 + recentRentals.Count * 8),
                                DetectedAt = now,
                                RecommendedAction = "Promote",
                                Priority = 3
                            });
                        }
                    }
                }
            }
            return results;
        }

        // ----------------------------------------------------------------
        //  Engine 6: Dormant Revival Detector
        // ----------------------------------------------------------------

        private List<CulturalMoment> DetectDormantRevivals(
            IReadOnlyList<Movie> movies,
            IReadOnlyList<Rental> rentals,
            DateTime now)
        {
            var results = new List<CulturalMoment>();
            var recentStart = now.AddDays(-_config.RecentWindowDays);
            var dormantCutoff = now.AddDays(-_config.DormantDaysThreshold);

            foreach (var movie in movies)
            {
                var movieRentals = rentals
                    .Where(r => r.MovieId == movie.Id)
                    .OrderByDescending(r => r.RentalDate)
                    .ToList();

                if (movieRentals.Count < 2) continue;

                var recentOnes = movieRentals.Where(r => r.RentalDate >= recentStart).ToList();
                if (recentOnes.Count == 0) continue;

                // Check if before recent window, there was a dormant gap
                var olderOnes = movieRentals.Where(r => r.RentalDate < recentStart).ToList();
                if (olderOnes.Count == 0) continue;

                var lastOlderRental = olderOnes.Max(r => r.RentalDate);
                var gapDays = (recentOnes.Min(r => r.RentalDate) - lastOlderRental).TotalDays;

                if (gapDays >= _config.DormantDaysThreshold)
                {
                    results.Add(new CulturalMoment
                    {
                        MovieId = movie.Id,
                        MovieName = movie.Name,
                        Genre = movie.Genre,
                        MomentType = "DormantRevival",
                        Description = string.Format("\"{0}\" revived after {1}-day dormancy with {2} new rental(s)",
                            movie.Name, (int)gapDays, recentOnes.Count),
                        RelevanceScore = Math.Min(100, 55 + recentOnes.Count * 15 + (gapDays > 120 ? 10 : 0)),
                        DetectedAt = now,
                        RecommendedAction = "Feature",
                        Priority = 2
                    });
                }
            }
            return results;
        }

        // ----------------------------------------------------------------
        //  Engine 7: Insight Generator
        // ----------------------------------------------------------------

        private List<string> GenerateInsights(
            List<CulturalMoment> moments,
            List<GenreMomentumEntry> genreMomentum,
            DateTime now)
        {
            var insights = new List<string>();

            if (moments.Count == 0)
            {
                insights.Add("No cultural moments detected — the catalog is in a quiet period.");
                return insights;
            }

            // Top moment
            var top = moments.First();
            insights.Add(string.Format("Strongest signal: {0} (relevance {1:F0}/100) — {2}",
                top.MomentType, top.RelevanceScore, top.Description));

            // Count by type
            var types = moments.GroupBy(m => m.MomentType).OrderByDescending(g => g.Count());
            var dominantType = types.First();
            insights.Add(string.Format("Dominant moment type: {0} ({1} occurrences)",
                dominantType.Key, dominantType.Count()));

            // Genre momentum
            var surging = genreMomentum.Where(g => g.Trend == "Surging").ToList();
            if (surging.Any())
            {
                insights.Add(string.Format("Genre surge detected: {0}",
                    string.Join(", ", surging.Select(s => string.Format("{0} ({1:F1}x)", s.Genre, s.MomentumRatio)))));
            }

            // Anniversaries
            var anniversaries = moments.Where(m => m.MomentType == "Anniversary").ToList();
            if (anniversaries.Count > 0)
            {
                insights.Add(string.Format("{0} milestone anniversary/anniversaries approaching — consider themed promotions",
                    anniversaries.Count));
            }

            // Dormant revivals
            var revivals = moments.Where(m => m.MomentType == "DormantRevival").ToList();
            if (revivals.Count > 0)
            {
                insights.Add(string.Format("{0} dormant title(s) showing signs of life — investigate external drivers",
                    revivals.Count));
            }

            // Priority 1 items
            var urgent = moments.Where(m => m.Priority == 1).ToList();
            if (urgent.Count > 0)
            {
                insights.Add(string.Format("{0} high-priority moment(s) require immediate attention", urgent.Count));
            }

            return insights;
        }

        // ----------------------------------------------------------------
        //  Health Score
        // ----------------------------------------------------------------

        private double ComputeHealthScore(List<CulturalMoment> moments, List<GenreMomentumEntry> genreMomentum)
        {
            double score = 50;

            if (moments.Any(m => m.MomentType == "Anniversary")) score += 10;
            if (moments.Any(m => m.MomentType == "FranchiseSurge")) score += 10;
            if (genreMomentum.Any(g => g.Trend == "Surging")) score += 10;
            if (moments.Any(m => m.MomentType == "DormantRevival")) score += 10;
            if (moments.Any(m => m.MomentType == "NostalgiaCycle")) score += 10;

            return Math.Min(100, score);
        }
    }
}
