using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Tracks movie lifecycle stages (NewRelease → Trending → Catalog → Archive)
    /// based on rental activity, age, and demand patterns. Provides pricing tier
    /// recommendations, retirement candidates, restock suggestions, and fleet-wide
    /// lifecycle analytics for inventory planning.
    /// </summary>
    public class MovieLifecycleService
    {
        private readonly IMovieRepository _movieRepo;
        private readonly IRentalRepository _rentalRepo;
        private readonly LifecycleConfig _config;

        public MovieLifecycleService(
            IMovieRepository movieRepo,
            IRentalRepository rentalRepo,
            LifecycleConfig config = null)
        {
            _movieRepo = movieRepo
                ?? throw new ArgumentNullException(nameof(movieRepo));
            _rentalRepo = rentalRepo
                ?? throw new ArgumentNullException(nameof(rentalRepo));
            _config = config ?? new LifecycleConfig();
        }

        // ── Single Movie ────────────────────────────────────────────

        /// <summary>
        /// Determine the lifecycle stage and full profile for a movie.
        /// </summary>
        public MovieLifecycleProfile GetProfile(int movieId, DateTime asOfDate)
        {
            var movie = _movieRepo.GetById(movieId);
            if (movie == null)
                throw new ArgumentException($"Movie {movieId} not found.");

            var rentals = _rentalRepo.GetAll()
                .Where(r => r.MovieId == movieId)
                .OrderBy(r => r.RentalDate)
                .ToList();

            return BuildProfile(movie, rentals, asOfDate);
        }

        /// <summary>
        /// Get lifecycle profiles for all movies.
        /// </summary>
        public IReadOnlyList<MovieLifecycleProfile> GetAllProfiles(DateTime asOfDate)
        {
            var movies = _movieRepo.GetAll();
            var allRentals = _rentalRepo.GetAll();
            var rentalsByMovie = allRentals
                .GroupBy(r => r.MovieId)
                .ToDictionary(g => g.Key, g => g.OrderBy(r => r.RentalDate).ToList());

            return movies
                .Select(m =>
                {
                    rentalsByMovie.TryGetValue(m.Id, out var rentals);
                    return BuildProfile(m, rentals ?? new List<Rental>(), asOfDate);
                })
                .OrderByDescending(p => p.DemandScore)
                .ToList();
        }

        // ── Pricing Recommendations ─────────────────────────────────

        /// <summary>
        /// Suggest a daily rate based on lifecycle stage and demand.
        /// </summary>
        public PricingRecommendation GetPricingRecommendation(int movieId, DateTime asOfDate)
        {
            var profile = GetProfile(movieId, asOfDate);
            return BuildPricingRecommendation(profile);
        }

        /// <summary>
        /// Get pricing recommendations for all movies in a given stage.
        /// </summary>
        public IReadOnlyList<PricingRecommendation> GetPricingByStage(
            LifecycleStage stage, DateTime asOfDate)
        {
            return GetAllProfiles(asOfDate)
                .Where(p => p.Stage == stage)
                .Select(BuildPricingRecommendation)
                .OrderByDescending(r => r.SuggestedRate)
                .ToList();
        }

        // ── Retirement & Restock ─────────────────────────────────────

        /// <summary>
        /// Identify movies that should be retired from inventory.
        /// </summary>
        public IReadOnlyList<RetirementCandidate> GetRetirementCandidates(DateTime asOfDate)
        {
            var profiles = GetAllProfiles(asOfDate);
            var candidates = new List<RetirementCandidate>();

            foreach (var p in profiles)
            {
                if (p.Stage != LifecycleStage.Archive)
                    continue;

                var reasons = new List<string>();

                if (p.DaysSinceLastRental > _config.RetirementInactiveDays)
                    reasons.Add($"No rentals in {p.DaysSinceLastRental} days");

                if (p.TotalRentals <= _config.RetirementMinRentals)
                    reasons.Add($"Only {p.TotalRentals} lifetime rental(s)");

                if (p.RecentRentals30d == 0 && p.RecentRentals90d <= 1)
                    reasons.Add("Near-zero recent demand");

                if (reasons.Count > 0)
                {
                    candidates.Add(new RetirementCandidate
                    {
                        MovieId = p.MovieId,
                        MovieName = p.MovieName,
                        Stage = p.Stage,
                        TotalRentals = p.TotalRentals,
                        DaysSinceLastRental = p.DaysSinceLastRental,
                        DemandScore = p.DemandScore,
                        Reasons = reasons,
                        Recommendation = p.TotalRentals == 0
                            ? "Remove — never rented"
                            : "Archive or discount clearance"
                    });
                }
            }

            return candidates.OrderBy(c => c.DemandScore).ToList();
        }

        /// <summary>
        /// Identify trending movies that may need more copies (restock).
        /// </summary>
        public IReadOnlyList<RestockSuggestion> GetRestockSuggestions(DateTime asOfDate)
        {
            var profiles = GetAllProfiles(asOfDate);
            var suggestions = new List<RestockSuggestion>();

            foreach (var p in profiles)
            {
                if (p.Stage != LifecycleStage.NewRelease && p.Stage != LifecycleStage.Trending)
                    continue;

                if (p.DemandScore < _config.RestockDemandThreshold)
                    continue;

                var urgency = p.DemandScore >= 80 ? "High"
                    : p.DemandScore >= 50 ? "Medium"
                    : "Low";

                suggestions.Add(new RestockSuggestion
                {
                    MovieId = p.MovieId,
                    MovieName = p.MovieName,
                    Stage = p.Stage,
                    DemandScore = p.DemandScore,
                    RecentRentals30d = p.RecentRentals30d,
                    Velocity = p.Velocity,
                    Urgency = urgency,
                    Reason = p.Stage == LifecycleStage.NewRelease
                        ? "New release with high demand"
                        : "Trending title — demand exceeds typical catalog levels"
                });
            }

            return suggestions.OrderByDescending(s => s.DemandScore).ToList();
        }

        // ── Fleet Analytics ──────────────────────────────────────────

        /// <summary>
        /// Generate a fleet-wide lifecycle summary with stage distribution,
        /// health metrics, and actionable insights.
        /// </summary>
        public LifecycleReport GetReport(DateTime asOfDate)
        {
            var profiles = GetAllProfiles(asOfDate);
            var byStage = profiles
                .GroupBy(p => p.Stage)
                .ToDictionary(g => g.Key, g => g.ToList());

            var stageBreakdown = new Dictionary<LifecycleStage, StageStats>();
            foreach (LifecycleStage stage in Enum.GetValues(typeof(LifecycleStage)))
            {
                var inStage = byStage.TryGetValue(stage, out var _v1) ? _v1 : new List<MovieLifecycleProfile>();
                stageBreakdown[stage] = new StageStats
                {
                    Count = inStage.Count,
                    AvgDemandScore = inStage.Count > 0
                        ? Math.Round(inStage.Average(p => p.DemandScore), 1)
                        : 0,
                    TotalRentals = inStage.Sum(p => p.TotalRentals),
                    AvgAge = inStage.Count > 0
                        ? Math.Round(inStage.Average(p => p.AgeDays), 0)
                        : 0
                };
            }

            var totalMovies = profiles.Count;
            var insights = new List<string>();

            // Health checks
            var archiveCount = stageBreakdown.ContainsKey(LifecycleStage.Archive)
                ? stageBreakdown[LifecycleStage.Archive].Count : 0;
            var newCount = stageBreakdown.ContainsKey(LifecycleStage.NewRelease)
                ? stageBreakdown[LifecycleStage.NewRelease].Count : 0;

            if (totalMovies > 0 && archiveCount > totalMovies * 0.5)
                insights.Add($"Over 50% of inventory ({archiveCount}/{totalMovies}) is archived — consider clearance or removal.");

            if (newCount == 0)
                insights.Add("No new releases in inventory — consider acquiring recent titles.");

            var trendingCount = stageBreakdown.ContainsKey(LifecycleStage.Trending)
                ? stageBreakdown[LifecycleStage.Trending].Count : 0;
            if (trendingCount > 0)
                insights.Add($"{trendingCount} movie(s) trending — ensure adequate stock.");

            var neverRented = profiles.Count(p => p.TotalRentals == 0);
            if (neverRented > 0)
                insights.Add($"{neverRented} movie(s) never rented — review placement or consider removal.");

            return new LifecycleReport
            {
                AsOfDate = asOfDate,
                TotalMovies = totalMovies,
                StageBreakdown = stageBreakdown,
                RetirementCandidates = GetRetirementCandidates(asOfDate).Count,
                RestockNeeded = GetRestockSuggestions(asOfDate).Count,
                NeverRented = neverRented,
                AvgDemandScore = totalMovies > 0
                    ? Math.Round(profiles.Average(p => p.DemandScore), 1)
                    : 0,
                Insights = insights,
                TopPerformers = profiles.Take(5).ToList(),
                BottomPerformers = profiles
                    .OrderBy(p => p.DemandScore)
                    .Take(5).ToList()
            };
        }

        // ── Transition Detection ─────────────────────────────────────

        /// <summary>
        /// Check which movies are near a stage transition boundary.
        /// Useful for planning pricing and marketing changes.
        /// </summary>
        public IReadOnlyList<TransitionAlert> GetTransitionAlerts(DateTime asOfDate)
        {
            var profiles = GetAllProfiles(asOfDate);
            var alerts = new List<TransitionAlert>();

            foreach (var p in profiles)
            {
                // NewRelease about to leave new release window
                if (p.Stage == LifecycleStage.NewRelease &&
                    p.AgeDays >= _config.NewReleaseDays - 7)
                {
                    alerts.Add(new TransitionAlert
                    {
                        MovieId = p.MovieId,
                        MovieName = p.MovieName,
                        CurrentStage = p.Stage,
                        PredictedStage = p.DemandScore >= _config.TrendingThreshold
                            ? LifecycleStage.Trending
                            : LifecycleStage.Catalog,
                        DaysUntilTransition = Math.Max(0, _config.NewReleaseDays - p.AgeDays),
                        Reason = "Leaving new release window soon"
                    });
                }

                // Trending about to drop to catalog
                if (p.Stage == LifecycleStage.Trending &&
                    p.DemandScore < _config.TrendingThreshold + 10)
                {
                    alerts.Add(new TransitionAlert
                    {
                        MovieId = p.MovieId,
                        MovieName = p.MovieName,
                        CurrentStage = p.Stage,
                        PredictedStage = LifecycleStage.Catalog,
                        DaysUntilTransition = -1, // demand-driven, not time-based
                        Reason = $"Demand score ({p.DemandScore:F0}) approaching catalog threshold ({_config.TrendingThreshold})"
                    });
                }

                // Catalog about to drop to archive
                if (p.Stage == LifecycleStage.Catalog &&
                    p.DemandScore < _config.ArchiveThreshold + 5)
                {
                    alerts.Add(new TransitionAlert
                    {
                        MovieId = p.MovieId,
                        MovieName = p.MovieName,
                        CurrentStage = p.Stage,
                        PredictedStage = LifecycleStage.Archive,
                        DaysUntilTransition = -1,
                        Reason = $"Demand score ({p.DemandScore:F0}) approaching archive threshold ({_config.ArchiveThreshold})"
                    });
                }
            }

            return alerts.OrderBy(a => a.DaysUntilTransition).ToList();
        }

        // ── Private Helpers ──────────────────────────────────────────

        private MovieLifecycleProfile BuildProfile(
            Movie movie, List<Rental> rentals, DateTime asOfDate)
        {
            var ageDays = movie.ReleaseDate.HasValue
                ? Math.Max(0, (int)(asOfDate - movie.ReleaseDate.Value).TotalDays)
                : 365; // assume 1 year if unknown

            var totalRentals = rentals.Count;

            var rentals30d = rentals
                .Count(r => (asOfDate - r.RentalDate).TotalDays <= 30);
            var rentals90d = rentals
                .Count(r => (asOfDate - r.RentalDate).TotalDays <= 90);

            var lastRentalDate = rentals.Count > 0
                ? rentals.Max(r => r.RentalDate)
                : (DateTime?)null;

            var daysSinceLastRental = lastRentalDate.HasValue
                ? (int)(asOfDate - lastRentalDate.Value).TotalDays
                : int.MaxValue;

            // Velocity: rentals per 30-day period over the last 90 days
            var velocity = rentals90d > 0
                ? Math.Round(rentals90d / 3.0, 2)
                : 0.0;

            // Demand score: weighted combination of recent activity and trends
            var demandScore = CalculateDemandScore(
                totalRentals, rentals30d, rentals90d,
                daysSinceLastRental, ageDays);

            // Determine stage
            var stage = ClassifyStage(ageDays, demandScore);

            // Revenue
            var totalRevenue = rentals.Sum(r => r.TotalCost);

            return new MovieLifecycleProfile
            {
                MovieId = movie.Id,
                MovieName = movie.Name,
                Genre = movie.Genre,
                ReleaseDate = movie.ReleaseDate,
                AgeDays = ageDays,
                Stage = stage,
                TotalRentals = totalRentals,
                RecentRentals30d = rentals30d,
                RecentRentals90d = rentals90d,
                DaysSinceLastRental = daysSinceLastRental == int.MaxValue ? -1 : daysSinceLastRental,
                Velocity = velocity,
                DemandScore = Math.Round(demandScore, 1),
                TotalRevenue = totalRevenue,
                CurrentDailyRate = movie.DailyRate
            };
        }

        private double CalculateDemandScore(
            int totalRentals, int rentals30d, int rentals90d,
            int daysSinceLastRental, int ageDays)
        {
            // Recent activity weight (0-40 pts): 30-day rentals
            var recentScore = Math.Min(40, rentals30d * 10.0);

            // Medium-term trend (0-25 pts): 90-day rentals
            var trendScore = Math.Min(25, rentals90d * 3.0);

            // Recency bonus (0-20 pts): how recently was it rented
            double recencyScore;
            if (daysSinceLastRental == int.MaxValue || daysSinceLastRental < 0)
                recencyScore = 0;
            else if (daysSinceLastRental <= 7)
                recencyScore = 20;
            else if (daysSinceLastRental <= 30)
                recencyScore = 15;
            else if (daysSinceLastRental <= 90)
                recencyScore = 8;
            else
                recencyScore = Math.Max(0, 5 - (daysSinceLastRental - 90) / 60.0);

            // Lifetime contribution (0-15 pts): log-scaled total
            var lifetimeScore = totalRentals > 0
                ? Math.Min(15, Math.Log(totalRentals + 1) * 5)
                : 0;

            return Math.Min(100, recentScore + trendScore + recencyScore + lifetimeScore);
        }

        private LifecycleStage ClassifyStage(int ageDays, double demandScore)
        {
            if (ageDays <= _config.NewReleaseDays)
                return LifecycleStage.NewRelease;

            if (demandScore >= _config.TrendingThreshold)
                return LifecycleStage.Trending;

            if (demandScore >= _config.ArchiveThreshold)
                return LifecycleStage.Catalog;

            return LifecycleStage.Archive;
        }

        private PricingRecommendation BuildPricingRecommendation(
            MovieLifecycleProfile profile)
        {
            decimal suggestedRate;
            string rationale;

            switch (profile.Stage)
            {
                case LifecycleStage.NewRelease:
                    suggestedRate = _config.NewReleaseRate;
                    rationale = "Premium pricing — new release window";
                    break;
                case LifecycleStage.Trending:
                    // Scale between catalog and new release based on demand
                    var trendFactor = (profile.DemandScore - _config.TrendingThreshold)
                        / (100 - _config.TrendingThreshold);
                    suggestedRate = _config.CatalogRate +
                        (decimal)trendFactor * (_config.NewReleaseRate - _config.CatalogRate);
                    suggestedRate = Math.Round(suggestedRate, 2);
                    rationale = $"Demand-scaled pricing (score: {profile.DemandScore:F0})";
                    break;
                case LifecycleStage.Catalog:
                    suggestedRate = _config.CatalogRate;
                    rationale = "Standard catalog pricing";
                    break;
                case LifecycleStage.Archive:
                default:
                    suggestedRate = _config.ArchiveRate;
                    rationale = "Discount pricing — low demand";
                    break;
            }

            var currentRate = profile.CurrentDailyRate ?? suggestedRate;
            var change = suggestedRate - currentRate;

            return new PricingRecommendation
            {
                MovieId = profile.MovieId,
                MovieName = profile.MovieName,
                Stage = profile.Stage,
                CurrentRate = currentRate,
                SuggestedRate = suggestedRate,
                PriceChange = change,
                Rationale = rationale,
                DemandScore = profile.DemandScore
            };
        }
    }

    // ── DTOs ──────────────────────────────────────────────────────

    /// <summary>
    /// Where a movie sits in its commercial lifecycle.
    /// </summary>
    public enum LifecycleStage
    {
        NewRelease = 1,
        Trending = 2,
        Catalog = 3,
        Archive = 4
    }

    /// <summary>
    /// Full lifecycle profile for a single movie.
    /// </summary>
    public class MovieLifecycleProfile
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public Genre? Genre { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public int AgeDays { get; set; }
        public LifecycleStage Stage { get; set; }
        public int TotalRentals { get; set; }
        public int RecentRentals30d { get; set; }
        public int RecentRentals90d { get; set; }
        public int DaysSinceLastRental { get; set; }
        public double Velocity { get; set; }
        public double DemandScore { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal? CurrentDailyRate { get; set; }
    }

    /// <summary>
    /// Pricing suggestion based on lifecycle stage and demand.
    /// </summary>
    public class PricingRecommendation
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public LifecycleStage Stage { get; set; }
        public decimal CurrentRate { get; set; }
        public decimal SuggestedRate { get; set; }
        public decimal PriceChange { get; set; }
        public string Rationale { get; set; }
        public double DemandScore { get; set; }
    }

    /// <summary>
    /// Movie flagged for possible removal from inventory.
    /// </summary>
    public class RetirementCandidate
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public LifecycleStage Stage { get; set; }
        public int TotalRentals { get; set; }
        public int DaysSinceLastRental { get; set; }
        public double DemandScore { get; set; }
        public IReadOnlyList<string> Reasons { get; set; }
        public string Recommendation { get; set; }
    }

    /// <summary>
    /// Suggestion to acquire more copies of a high-demand movie.
    /// </summary>
    public class RestockSuggestion
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public LifecycleStage Stage { get; set; }
        public double DemandScore { get; set; }
        public int RecentRentals30d { get; set; }
        public double Velocity { get; set; }
        public string Urgency { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// Alert that a movie is near a lifecycle stage transition.
    /// </summary>
    public class TransitionAlert
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public LifecycleStage CurrentStage { get; set; }
        public LifecycleStage PredictedStage { get; set; }
        public int DaysUntilTransition { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// Lifecycle stage distribution stats.
    /// </summary>
    public class StageStats
    {
        public int Count { get; set; }
        public double AvgDemandScore { get; set; }
        public int TotalRentals { get; set; }
        public double AvgAge { get; set; }
    }

    /// <summary>
    /// Fleet-wide lifecycle health report.
    /// </summary>
    public class LifecycleReport
    {
        public DateTime AsOfDate { get; set; }
        public int TotalMovies { get; set; }
        public Dictionary<LifecycleStage, StageStats> StageBreakdown { get; set; }
        public int RetirementCandidates { get; set; }
        public int RestockNeeded { get; set; }
        public int NeverRented { get; set; }
        public double AvgDemandScore { get; set; }
        public IReadOnlyList<string> Insights { get; set; }
        public IReadOnlyList<MovieLifecycleProfile> TopPerformers { get; set; }
        public IReadOnlyList<MovieLifecycleProfile> BottomPerformers { get; set; }
    }

    /// <summary>
    /// Configurable thresholds for lifecycle classification and pricing.
    /// </summary>
    public class LifecycleConfig
    {
        /// <summary>Days after release date a movie is considered "new".</summary>
        public int NewReleaseDays { get; set; } = 90;

        /// <summary>Demand score threshold for "trending" status.</summary>
        public double TrendingThreshold { get; set; } = 40;

        /// <summary>Demand score below which a movie is "archived".</summary>
        public double ArchiveThreshold { get; set; } = 10;

        /// <summary>Daily rate for new releases.</summary>
        public decimal NewReleaseRate { get; set; } = 4.99m;

        /// <summary>Daily rate for standard catalog titles.</summary>
        public decimal CatalogRate { get; set; } = 2.99m;

        /// <summary>Daily rate for archived/discount titles.</summary>
        public decimal ArchiveRate { get; set; } = 0.99m;

        /// <summary>Days without a rental before retirement consideration.</summary>
        public int RetirementInactiveDays { get; set; } = 180;

        /// <summary>Minimum lifetime rentals below which retirement is suggested.</summary>
        public int RetirementMinRentals { get; set; } = 2;

        /// <summary>Minimum demand score to trigger restock suggestion.</summary>
        public double RestockDemandThreshold { get; set; } = 30;
    }
}
