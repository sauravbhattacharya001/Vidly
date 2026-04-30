using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Autonomous Catalog Velocity Engine — tracks movie lifecycle phases,
    /// computes rental velocity and acceleration, detects phase transitions,
    /// and recommends autonomous actions (promote/discount/bundle/retire).
    /// </summary>
    public class CatalogVelocityService
    {
        private readonly IMovieRepository _movieRepository;
        private readonly IRentalRepository _rentalRepository;
        private readonly IClock _clock;
        private readonly VelocityEngineConfig _config;

        // Phase history for transition detection (static for simplicity, like other services)
        private static readonly Dictionary<int, CatalogPhase> _previousPhases = new Dictionary<int, CatalogPhase>();
        private static readonly object _lock = new object();

        public CatalogVelocityService(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository,
            IClock clock,
            VelocityEngineConfig config = null)
        {
            if (movieRepository == null) throw new ArgumentNullException("movieRepository");
            if (rentalRepository == null) throw new ArgumentNullException("rentalRepository");
            if (clock == null) throw new ArgumentNullException("clock");
            _movieRepository = movieRepository;
            _rentalRepository = rentalRepository;
            _clock = clock;
            _config = config ?? new VelocityEngineConfig();
        }

        /// <summary>Reset static state (for testing).</summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _previousPhases.Clear();
            }
        }

        /// <summary>
        /// Generate a full catalog velocity report with all profiles, transitions, and insights.
        /// </summary>
        public CatalogVelocityReport Analyze()
        {
            var now = _clock.Now;
            var allMovies = _movieRepository.GetAll();
            var allRentals = _rentalRepository.GetAll();
            var windowStart = now.AddDays(-_config.WindowDays);

            var windowRentals = allRentals
                .Where(r => r.RentalDate >= windowStart)
                .ToList();

            // Compute per-movie profiles
            var profiles = new List<MovieVelocityProfile>();
            foreach (var movie in allMovies)
            {
                var profile = BuildProfile(movie, windowRentals, allRentals, now);
                profiles.Add(profile);
            }

            // Normalize velocity scores relative to catalog
            NormalizeVelocities(profiles);

            // Assign phases and actions
            foreach (var p in profiles)
            {
                p.Phase = DeterminePhase(p, now);
                DetectTransition(p);
                var (action, confidence, reasoning) = RecommendAction(p);
                p.RecommendedAction = action;
                p.ActionConfidence = confidence;
                p.ActionReasoning = reasoning;
                p.AtRisk = IsAtRisk(p);
                p.EstimatedDaysToNextPhase = EstimateDaysToTransition(p);
            }

            // Build report
            var transitions = DetectAllTransitions(profiles);
            var genreBreakdown = BuildGenreBreakdown(profiles);
            var urgentActions = profiles
                .Where(p => p.RecommendedAction != VelocityAction.None && p.ActionConfidence >= 0.7)
                .OrderByDescending(p => p.ActionConfidence)
                .Take(10)
                .ToList();

            var report = new CatalogVelocityReport
            {
                GeneratedAt = now,
                WindowDays = _config.WindowDays,
                Profiles = profiles,
                PhaseDistribution = profiles.GroupBy(p => p.Phase)
                    .ToDictionary(g => g.Key, g => g.Count()),
                CatalogHealthScore = ComputeCatalogHealth(profiles),
                AverageVelocity = profiles.Any() ? profiles.Average(p => p.VelocityScore) : 0,
                UrgentActions = urgentActions,
                RecentTransitions = transitions,
                GenreBreakdown = genreBreakdown,
                Insights = GenerateInsights(profiles, genreBreakdown, transitions)
            };

            return report;
        }

        /// <summary>
        /// Get velocity profile for a single movie.
        /// </summary>
        public MovieVelocityProfile GetMovieVelocity(int movieId)
        {
            var report = Analyze();
            return report.Profiles.FirstOrDefault(p => p.MovieId == movieId);
        }

        /// <summary>
        /// Get movies in a specific lifecycle phase.
        /// </summary>
        public List<MovieVelocityProfile> GetByPhase(CatalogPhase phase)
        {
            var report = Analyze();
            return report.Profiles.Where(p => p.Phase == phase).ToList();
        }

        /// <summary>
        /// Get all movies with pending action recommendations.
        /// </summary>
        public List<MovieVelocityProfile> GetActionQueue()
        {
            var report = Analyze();
            return report.Profiles
                .Where(p => p.RecommendedAction != VelocityAction.None)
                .OrderByDescending(p => p.ActionConfidence)
                .ToList();
        }

        // --- Private Implementation ---

        private MovieVelocityProfile BuildProfile(Movie movie, List<Rental> windowRentals,
            IReadOnlyList<Rental> allRentals, DateTime now)
        {
            var movieRentals = windowRentals.Where(r => r.MovieId == movie.Id).ToList();
            var allMovieRentals = allRentals.Where(r => r.MovieId == movie.Id).ToList();

            // Recent period: last 7 days
            var recentStart = now.AddDays(-7);
            var recentRentals = movieRentals.Count(r => r.RentalDate >= recentStart);

            // Prior period: 8-14 days ago
            var priorStart = now.AddDays(-14);
            var priorRentals = movieRentals.Count(r => r.RentalDate >= priorStart && r.RentalDate < recentStart);

            // Days since last rental
            var lastRental = allMovieRentals.OrderByDescending(r => r.RentalDate).FirstOrDefault();
            var daysSinceLastRental = lastRental != null
                ? (int)Math.Ceiling((now - lastRental.RentalDate).TotalDays)
                : 9999;

            // Days in catalog
            var firstAppearance = movie.ReleaseDate ?? allMovieRentals
                .OrderBy(r => r.RentalDate)
                .Select(r => (DateTime?)r.RentalDate)
                .FirstOrDefault() ?? now;
            var daysInCatalog = Math.Max(1, (int)(now - firstAppearance).TotalDays);

            // Raw velocity = rentals per week in window
            var weeksInWindow = Math.Max(1.0, _config.WindowDays / 7.0);
            var rawVelocity = movieRentals.Count / weeksInWindow;

            // Acceleration = recent vs prior period rate change
            var acceleration = recentRentals - priorRentals;

            return new MovieVelocityProfile
            {
                MovieId = movie.Id,
                MovieName = movie.Name,
                Genre = movie.Genre,
                VelocityScore = rawVelocity, // Will be normalized later
                Acceleration = acceleration,
                RentalsInWindow = movieRentals.Count,
                RecentRentals = recentRentals,
                PriorPeriodRentals = priorRentals,
                DaysSinceLastRental = daysSinceLastRental,
                DaysInCatalog = daysInCatalog
            };
        }

        private void NormalizeVelocities(List<MovieVelocityProfile> profiles)
        {
            if (!profiles.Any()) return;

            var maxVelocity = profiles.Max(p => p.VelocityScore);
            if (maxVelocity <= 0)
            {
                foreach (var p in profiles) p.VelocityScore = 0;
                return;
            }

            foreach (var p in profiles)
            {
                p.VelocityScore = Math.Round((p.VelocityScore / maxVelocity) * 100.0, 1);
            }
        }

        private CatalogPhase DeterminePhase(MovieVelocityProfile profile, DateTime now)
        {
            // New arrivals
            if (profile.DaysInCatalog <= _config.NewArrivalDays)
                return CatalogPhase.NewArrival;

            // Dormant — no rentals in threshold period
            if (profile.DaysSinceLastRental >= _config.DormantThresholdDays && profile.RentalsInWindow == 0)
                return CatalogPhase.Dormant;

            // Resurgent — was dormant/declining but now accelerating
            CatalogPhase previousPhase;
            lock (_lock)
            {
                _previousPhases.TryGetValue(profile.MovieId, out previousPhase);
            }
            if ((previousPhase == CatalogPhase.Dormant || previousPhase == CatalogPhase.Declining)
                && profile.Acceleration >= _config.ResurgenceAccelerationThreshold)
                return CatalogPhase.Resurgent;

            // Hot
            if (profile.VelocityScore >= _config.HotThreshold)
                return CatalogPhase.Hot;

            // Declining
            if (profile.VelocityScore <= _config.DecliningThreshold && profile.Acceleration < 0)
                return CatalogPhase.Declining;

            // Default: Steady
            return CatalogPhase.Steady;
        }

        private void DetectTransition(MovieVelocityProfile profile)
        {
            lock (_lock)
            {
                CatalogPhase prev;
                if (_previousPhases.TryGetValue(profile.MovieId, out prev))
                {
                    if (prev != profile.Phase)
                        profile.PreviousPhase = prev;
                }
                _previousPhases[profile.MovieId] = profile.Phase;
            }
        }

        private List<PhaseTransition> DetectAllTransitions(List<MovieVelocityProfile> profiles)
        {
            return profiles
                .Where(p => p.PreviousPhase.HasValue && p.PreviousPhase.Value != p.Phase)
                .Select(p => new PhaseTransition
                {
                    MovieId = p.MovieId,
                    MovieName = p.MovieName,
                    FromPhase = p.PreviousPhase.Value,
                    ToPhase = p.Phase,
                    Trigger = GetTransitionTrigger(p)
                })
                .ToList();
        }

        private string GetTransitionTrigger(MovieVelocityProfile p)
        {
            if (p.Phase == CatalogPhase.Hot)
                return string.Format("Velocity reached {0:F0} (threshold: {1})", p.VelocityScore, _config.HotThreshold);
            if (p.Phase == CatalogPhase.Dormant)
                return string.Format("No rentals for {0} days", p.DaysSinceLastRental);
            if (p.Phase == CatalogPhase.Declining)
                return string.Format("Velocity dropped to {0:F0} with negative acceleration", p.VelocityScore);
            if (p.Phase == CatalogPhase.Resurgent)
                return string.Format("Acceleration spike of {0:+0;-0} from dormant/declining state", p.Acceleration);
            return "Phase criteria met";
        }

        private Tuple<VelocityAction, double, string> RecommendAction(MovieVelocityProfile profile)
        {
            switch (profile.Phase)
            {
                case CatalogPhase.Hot:
                    if (profile.Acceleration > 0)
                        return Tuple.Create(VelocityAction.Restock, 0.8,
                            string.Format("High demand with positive acceleration ({0:+0}). Consider additional copies.", profile.Acceleration));
                    return Tuple.Create(VelocityAction.Promote, 0.7,
                        "High velocity — feature prominently to maximize rental revenue.");

                case CatalogPhase.Declining:
                    if (profile.VelocityScore < 15)
                        return Tuple.Create(VelocityAction.Discount, 0.8,
                            string.Format("Low velocity ({0:F0}) with negative trend. Discount to stimulate demand.", profile.VelocityScore));
                    return Tuple.Create(VelocityAction.Bundle, 0.6,
                        "Moderate decline — bundle with popular titles for cross-exposure.");

                case CatalogPhase.Dormant:
                    if (profile.DaysSinceLastRental > 60)
                        return Tuple.Create(VelocityAction.Retire, 0.75,
                            string.Format("No rentals in {0} days. Strong retirement candidate.", profile.DaysSinceLastRental));
                    return Tuple.Create(VelocityAction.Discount, 0.6,
                        "Recently dormant — try discount before retiring.");

                case CatalogPhase.Resurgent:
                    return Tuple.Create(VelocityAction.Promote, 0.85,
                        string.Format("Demand revival detected (acceleration {0:+0}). Promote immediately.", profile.Acceleration));

                case CatalogPhase.NewArrival:
                    if (profile.VelocityScore < 20 && profile.DaysInCatalog > 14)
                        return Tuple.Create(VelocityAction.Promote, 0.5,
                            "New arrival with low initial traction. Needs visibility boost.");
                    return Tuple.Create(VelocityAction.None, 0.0, "New arrival — monitoring.");

                case CatalogPhase.Steady:
                    if (profile.Acceleration < -2)
                        return Tuple.Create(VelocityAction.Investigate, 0.5,
                            "Steady phase but accelerating downward. Worth investigating.");
                    return Tuple.Create(VelocityAction.None, 0.0, "Healthy steady state — no action needed.");

                default:
                    return Tuple.Create(VelocityAction.None, 0.0, "No action applicable.");
            }
        }

        private bool IsAtRisk(MovieVelocityProfile profile)
        {
            // At risk if declining with high negative acceleration, or steady with dropping velocity
            if (profile.Phase == CatalogPhase.Declining) return true;
            if (profile.Phase == CatalogPhase.Steady && profile.Acceleration <= -2) return true;
            if (profile.Phase == CatalogPhase.NewArrival && profile.DaysInCatalog > 21 && profile.RentalsInWindow == 0)
                return true;
            return false;
        }

        private int? EstimateDaysToTransition(MovieVelocityProfile profile)
        {
            if (profile.Acceleration == 0) return null;

            switch (profile.Phase)
            {
                case CatalogPhase.Hot:
                    if (profile.Acceleration < 0)
                    {
                        // Estimate days until velocity drops below hot threshold
                        var weeklyDrop = Math.Abs(profile.Acceleration);
                        var scoreDelta = profile.VelocityScore - _config.HotThreshold;
                        if (weeklyDrop > 0 && scoreDelta > 0)
                            return (int)Math.Ceiling((scoreDelta / weeklyDrop) * 7);
                    }
                    return null;

                case CatalogPhase.Steady:
                    if (profile.Acceleration < 0)
                    {
                        var weeklyDrop = Math.Abs(profile.Acceleration);
                        var scoreDelta = profile.VelocityScore - _config.DecliningThreshold;
                        if (weeklyDrop > 0 && scoreDelta > 0)
                            return (int)Math.Ceiling((scoreDelta / weeklyDrop) * 7);
                    }
                    return null;

                default:
                    return null;
            }
        }

        private double ComputeCatalogHealth(List<MovieVelocityProfile> profiles)
        {
            if (!profiles.Any()) return 0;

            var total = (double)profiles.Count;

            // Health factors:
            // 1. Proportion of active movies (not dormant) — 30%
            var activeRatio = profiles.Count(p => p.Phase != CatalogPhase.Dormant) / total;

            // 2. Average velocity — 25%
            var avgVelocity = profiles.Average(p => p.VelocityScore) / 100.0;

            // 3. Phase diversity (healthy mix) — 20%
            var phaseCount = profiles.Select(p => p.Phase).Distinct().Count();
            var phaseDiversity = Math.Min(1.0, phaseCount / 4.0);

            // 4. Proportion with positive or zero acceleration — 15%
            var growingRatio = profiles.Count(p => p.Acceleration >= 0) / total;

            // 5. Low at-risk ratio — 10%
            var safeRatio = profiles.Count(p => !p.AtRisk) / total;

            var score = (activeRatio * 30 + avgVelocity * 25 + phaseDiversity * 20 + growingRatio * 15 + safeRatio * 10);
            return Math.Round(Math.Min(100, Math.Max(0, score)), 1);
        }

        private List<GenreVelocitySummary> BuildGenreBreakdown(List<MovieVelocityProfile> profiles)
        {
            return profiles
                .Where(p => p.Genre.HasValue)
                .GroupBy(p => p.Genre.Value)
                .Select(g => new GenreVelocitySummary
                {
                    Genre = g.Key,
                    MovieCount = g.Count(),
                    AverageVelocity = Math.Round(g.Average(p => p.VelocityScore), 1),
                    HotCount = g.Count(p => p.Phase == CatalogPhase.Hot),
                    DormantCount = g.Count(p => p.Phase == CatalogPhase.Dormant),
                    HealthScore = Math.Round(ComputeGenreHealth(g.ToList()), 1)
                })
                .OrderByDescending(g => g.AverageVelocity)
                .ToList();
        }

        private double ComputeGenreHealth(List<MovieVelocityProfile> genreProfiles)
        {
            if (!genreProfiles.Any()) return 0;
            var total = (double)genreProfiles.Count;
            var activeRatio = genreProfiles.Count(p => p.Phase != CatalogPhase.Dormant) / total;
            var avgVelocity = genreProfiles.Average(p => p.VelocityScore) / 100.0;
            var hotRatio = genreProfiles.Count(p => p.Phase == CatalogPhase.Hot) / total;
            return Math.Min(100, (activeRatio * 40 + avgVelocity * 35 + hotRatio * 25) * 100.0 / 100.0);
        }

        private List<string> GenerateInsights(List<MovieVelocityProfile> profiles,
            List<GenreVelocitySummary> genreBreakdown, List<PhaseTransition> transitions)
        {
            var insights = new List<string>();

            if (!profiles.Any()) return insights;

            // Dormancy insight
            var dormantPct = profiles.Count(p => p.Phase == CatalogPhase.Dormant) * 100.0 / profiles.Count;
            if (dormantPct > 30)
                insights.Add(string.Format("⚠️ {0:F0}% of catalog is dormant. Consider a retirement sweep to free shelf space.", dormantPct));

            // Hot concentration
            var hotMovies = profiles.Where(p => p.Phase == CatalogPhase.Hot).ToList();
            if (hotMovies.Any())
            {
                var hotGenres = hotMovies.Where(p => p.Genre.HasValue).GroupBy(p => p.Genre.Value)
                    .OrderByDescending(g => g.Count()).FirstOrDefault();
                if (hotGenres != null)
                    insights.Add(string.Format("🔥 {0} dominates hot titles ({1} of {2}). Stock up on this genre.",
                        hotGenres.Key, hotGenres.Count(), hotMovies.Count));
            }

            // Resurgence opportunity
            var resurgent = profiles.Count(p => p.Phase == CatalogPhase.Resurgent);
            if (resurgent > 0)
                insights.Add(string.Format("📈 {0} movie(s) showing resurgence — promote immediately for revenue spike.", resurgent));

            // New arrivals struggling
            var strugglingNew = profiles.Count(p => p.Phase == CatalogPhase.NewArrival && p.AtRisk);
            if (strugglingNew > 0)
                insights.Add(string.Format("🆕 {0} new arrival(s) failing to gain traction. Need visibility boost.", strugglingNew));

            // Genre imbalance
            if (genreBreakdown.Any())
            {
                var topGenre = genreBreakdown.First();
                var bottomGenre = genreBreakdown.Last();
                if (topGenre.AverageVelocity > bottomGenre.AverageVelocity * 3 && bottomGenre.MovieCount > 1)
                    insights.Add(string.Format("📊 Genre velocity gap: {0} ({1:F0}) vs {2} ({3:F0}). Rebalance catalog investment.",
                        topGenre.Genre, topGenre.AverageVelocity, bottomGenre.Genre, bottomGenre.AverageVelocity));
            }

            // Transition alerts
            var declineTransitions = transitions.Count(t => t.ToPhase == CatalogPhase.Declining);
            if (declineTransitions > 2)
                insights.Add(string.Format("⬇️ {0} movies entered declining phase — possible seasonal shift or competition.", declineTransitions));

            // Average acceleration
            var avgAccel = profiles.Average(p => p.Acceleration);
            if (avgAccel < -1)
                insights.Add(string.Format("📉 Catalog-wide deceleration ({0:+0.0;-0.0} avg). Investigate external factors.", avgAccel));
            else if (avgAccel > 1)
                insights.Add(string.Format("🚀 Catalog-wide acceleration ({0:+0.0} avg). Capitalize on momentum.", avgAccel));

            return insights;
        }
    }
}
