using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    // ================================================================
    //  Rental Streak Engine — autonomous customer engagement streak
    //  tracker with proactive intervention recommendations.
    //
    //  7 engines:
    //  1. Streak Calculator — weekly rental streak detection
    //  2. Streak Risk Detector — at-risk streak identification
    //  3. Milestone Detector — Bronze/Silver/Gold/Platinum/Diamond
    //  4. Rescue Recommender — genre-based movie suggestions
    //  5. Engagement Scorer — composite 0-100 per customer
    //  6. Fleet Health Scorer — store-wide streak health
    //  7. Insight Generator — natural-language insights
    // ================================================================

    #region Models

    /// <summary>
    /// Tracks a single customer's rental streak data.
    /// </summary>
    public class CustomerStreak
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int CurrentStreakWeeks { get; set; }
        public int LongestStreakWeeks { get; set; }
        public int TotalActiveWeeks { get; set; }
        public DateTime? CurrentStreakStartDate { get; set; }
        public DateTime? LastRentalDate { get; set; }
        public double EngagementScore { get; set; }
        public bool HasActiveStreak { get; set; }
    }

    /// <summary>
    /// Represents a streak that is at risk of breaking.
    /// </summary>
    public class AtRiskStreak
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int CurrentStreakWeeks { get; set; }
        public double DaysRemaining { get; set; }
        public string RiskLevel { get; set; }
        public string Urgency { get; set; }
    }

    /// <summary>
    /// Represents a streak milestone achievement.
    /// </summary>
    public class StreakMilestone
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int StreakWeeks { get; set; }
        public string MilestoneName { get; set; }
        public string Tier { get; set; }
    }

    /// <summary>
    /// A rescue recommendation for an at-risk streak.
    /// </summary>
    public class RescueRecommendation
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public string Genre { get; set; }
        public string Reason { get; set; }
        public int StreakAtRisk { get; set; }
    }

    /// <summary>
    /// Fleet-wide streak health metrics.
    /// </summary>
    public class FleetStreakHealth
    {
        public double AverageEngagementScore { get; set; }
        public double ActiveStreakPercentage { get; set; }
        public int TotalCustomers { get; set; }
        public int CustomersWithActiveStreaks { get; set; }
        public int CustomersAtRisk { get; set; }
        public Dictionary<string, int> StreakDistribution { get; set; }
        public double HealthScore { get; set; }
        public string HealthTier { get; set; }
    }

    /// <summary>
    /// Full streak analysis report.
    /// </summary>
    public class StreakReport
    {
        public DateTime GeneratedAt { get; set; }
        public List<CustomerStreak> Streaks { get; set; }
        public List<AtRiskStreak> AtRiskStreaks { get; set; }
        public List<StreakMilestone> Milestones { get; set; }
        public List<RescueRecommendation> Recommendations { get; set; }
        public FleetStreakHealth FleetHealth { get; set; }
        public List<string> Insights { get; set; }
        public double OverallEngagementScore { get; set; }
    }

    /// <summary>
    /// Configuration for the streak engine.
    /// </summary>
    public class StreakConfig
    {
        public int MinStreakWeeks { get; set; }
        public int AtRiskDaysThreshold { get; set; }
        public int RecentWindowWeeks { get; set; }
        public int MaxRecommendationsPerCustomer { get; set; }

        public StreakConfig()
        {
            MinStreakWeeks = 2;
            AtRiskDaysThreshold = 3;
            RecentWindowWeeks = 12;
            MaxRecommendationsPerCustomer = 3;
        }
    }

    #endregion

    /// <summary>
    /// Autonomous customer engagement streak tracker with 7 analysis engines.
    /// </summary>
    public class RentalStreakService
    {
        private readonly IRentalRepository _rentalRepo;
        private readonly ICustomerRepository _customerRepo;
        private readonly IMovieRepository _movieRepo;
        private readonly IClock _clock;
        private readonly StreakConfig _config;

        private static readonly Dictionary<string, int> MilestoneThresholds = new Dictionary<string, int>
        {
            { "Diamond", 52 },
            { "Platinum", 26 },
            { "Gold", 12 },
            { "Silver", 8 },
            { "Bronze", 4 }
        };

        /// <summary>
        /// Creates a new RentalStreakService.
        /// </summary>
        public RentalStreakService(
            IRentalRepository rentalRepo,
            ICustomerRepository customerRepo,
            IMovieRepository movieRepo,
            IClock clock,
            StreakConfig config = null)
        {
            if (rentalRepo == null) throw new ArgumentNullException("rentalRepo");
            if (customerRepo == null) throw new ArgumentNullException("customerRepo");
            if (movieRepo == null) throw new ArgumentNullException("movieRepo");
            if (clock == null) throw new ArgumentNullException("clock");
            _rentalRepo = rentalRepo;
            _customerRepo = customerRepo;
            _movieRepo = movieRepo;
            _clock = clock;
            _config = config ?? new StreakConfig();
        }

        /// <summary>
        /// Runs all 7 engines and produces a full streak report.
        /// </summary>
        public StreakReport Analyze()
        {
            var streaks = CalculateStreaks();
            var atRisk = DetectAtRiskStreaks(streaks);
            var milestones = DetectMilestones(streaks);
            var recommendations = GenerateRescueRecommendations(atRisk);
            var fleet = CalculateFleetHealth(streaks, atRisk);
            var insights = GenerateInsights(streaks, atRisk, milestones, fleet);

            var avgEngagement = streaks.Count > 0
                ? streaks.Average(s => s.EngagementScore)
                : 0.0;

            return new StreakReport
            {
                GeneratedAt = _clock.Now,
                Streaks = streaks,
                AtRiskStreaks = atRisk,
                Milestones = milestones,
                Recommendations = recommendations,
                FleetHealth = fleet,
                Insights = insights,
                OverallEngagementScore = Math.Round(avgEngagement, 1)
            };
        }

        // ── Engine 1: Streak Calculator ─────────────────────────────

        /// <summary>
        /// Calculates rental streaks for all customers.
        /// </summary>
        public List<CustomerStreak> CalculateStreaks()
        {
            var customers = _customerRepo.GetAll();
            var allRentals = _rentalRepo.GetAll();
            var now = _clock.Now;
            var currentIsoWeek = GetIsoWeekNumber(now);
            var currentYear = ISOWeekYear(now);

            var result = new List<CustomerStreak>();

            foreach (var customer in customers)
            {
                var rentals = allRentals
                    .Where(r => r.CustomerId == customer.Id)
                    .OrderBy(r => r.RentalDate)
                    .ToList();

                if (rentals.Count == 0)
                {
                    result.Add(new CustomerStreak
                    {
                        CustomerId = customer.Id,
                        CustomerName = customer.Name,
                        CurrentStreakWeeks = 0,
                        LongestStreakWeeks = 0,
                        TotalActiveWeeks = 0,
                        HasActiveStreak = false,
                        EngagementScore = 0
                    });
                    continue;
                }

                // Get distinct (year, week) tuples
                var activeWeeks = rentals
                    .Select(r => new { Year = ISOWeekYear(r.RentalDate), Week = GetIsoWeekNumber(r.RentalDate) })
                    .Distinct()
                    .OrderBy(w => w.Year)
                    .ThenBy(w => w.Week)
                    .ToList();

                int totalActiveWeeks = activeWeeks.Count;

                // Find streaks of consecutive weeks
                int currentStreak = 0;
                int longestStreak = 0;
                int runLength = 1;
                bool currentStreakIsActive = false;

                for (int i = 1; i < activeWeeks.Count; i++)
                {
                    var prev = activeWeeks[i - 1];
                    var curr = activeWeeks[i];

                    if (AreConsecutiveWeeks(prev.Year, prev.Week, curr.Year, curr.Week))
                    {
                        runLength++;
                    }
                    else
                    {
                        if (runLength > longestStreak) longestStreak = runLength;
                        runLength = 1;
                    }
                }
                if (runLength > longestStreak) longestStreak = runLength;

                // Check if the last active week is current or previous week (streak still alive)
                var lastActiveWeek = activeWeeks.Last();
                bool isCurrentWeek = lastActiveWeek.Year == currentYear && lastActiveWeek.Week == currentIsoWeek;
                var prevWeekDate = now.AddDays(-7);
                int prevYear = ISOWeekYear(prevWeekDate);
                int prevWeek = GetIsoWeekNumber(prevWeekDate);
                bool isPreviousWeek = lastActiveWeek.Year == prevYear && lastActiveWeek.Week == prevWeek;

                if (isCurrentWeek || isPreviousWeek)
                {
                    // Count backwards from the last active week
                    currentStreak = 1;
                    for (int i = activeWeeks.Count - 2; i >= 0; i--)
                    {
                        if (AreConsecutiveWeeks(activeWeeks[i].Year, activeWeeks[i].Week,
                            activeWeeks[i + 1].Year, activeWeeks[i + 1].Week))
                        {
                            currentStreak++;
                        }
                        else break;
                    }
                    currentStreakIsActive = true;
                }

                // Engagement scoring
                var lastRental = rentals.Last().RentalDate;
                var genres = rentals.Select(r =>
                {
                    var movie = _movieRepo.GetAll().FirstOrDefault(m => m.Id == r.MovieId);
                    return movie != null ? movie.Genre : (Genre?)null;
                }).Where(g => g.HasValue).Select(g => g.Value).ToList();

                double engagementScore = CalculateEngagementScore(
                    currentStreakIsActive ? currentStreak : 0,
                    totalActiveWeeks,
                    _config.RecentWindowWeeks,
                    genres,
                    lastRental,
                    now);

                DateTime? streakStart = null;
                if (currentStreakIsActive && currentStreak > 0)
                {
                    // Walk back from last active week
                    int startIdx = activeWeeks.Count - currentStreak;
                    var sw = activeWeeks[startIdx];
                    streakStart = FirstDayOfIsoWeek(sw.Year, sw.Week);
                }

                result.Add(new CustomerStreak
                {
                    CustomerId = customer.Id,
                    CustomerName = customer.Name,
                    CurrentStreakWeeks = currentStreakIsActive ? currentStreak : 0,
                    LongestStreakWeeks = longestStreak,
                    TotalActiveWeeks = totalActiveWeeks,
                    CurrentStreakStartDate = streakStart,
                    LastRentalDate = lastRental,
                    EngagementScore = engagementScore,
                    HasActiveStreak = currentStreakIsActive && currentStreak >= _config.MinStreakWeeks
                });
            }

            return result;
        }

        // ── Engine 2: Streak Risk Detector ──────────────────────────

        /// <summary>
        /// Detects customers whose active streaks are at risk.
        /// </summary>
        public List<AtRiskStreak> DetectAtRiskStreaks()
        {
            return DetectAtRiskStreaks(CalculateStreaks());
        }

        private List<AtRiskStreak> DetectAtRiskStreaks(List<CustomerStreak> streaks)
        {
            var now = _clock.Now;
            var currentYear = ISOWeekYear(now);
            var currentWeek = GetIsoWeekNumber(now);

            var result = new List<AtRiskStreak>();

            foreach (var streak in streaks)
            {
                if (streak.CurrentStreakWeeks < _config.MinStreakWeeks) continue;

                // Check if they've rented this week
                var rentals = _rentalRepo.GetAll()
                    .Where(r => r.CustomerId == streak.CustomerId)
                    .ToList();

                bool hasRentalThisWeek = rentals.Any(r =>
                    ISOWeekYear(r.RentalDate) == currentYear &&
                    GetIsoWeekNumber(r.RentalDate) == currentWeek);

                if (hasRentalThisWeek) continue;

                // Calculate days remaining in current week
                var endOfWeek = EndOfIsoWeek(currentYear, currentWeek);
                var daysRemaining = (endOfWeek - now).TotalDays;

                if (daysRemaining > _config.AtRiskDaysThreshold) continue;

                string riskLevel;
                string urgency;
                if (daysRemaining < 1)
                {
                    riskLevel = "High";
                    urgency = "Streak expires today!";
                }
                else if (daysRemaining <= 2)
                {
                    riskLevel = "Medium";
                    urgency = string.Format("{0:F0} days to save streak", daysRemaining);
                }
                else
                {
                    riskLevel = "Low";
                    urgency = string.Format("{0:F0} days remaining this week", daysRemaining);
                }

                result.Add(new AtRiskStreak
                {
                    CustomerId = streak.CustomerId,
                    CustomerName = streak.CustomerName,
                    CurrentStreakWeeks = streak.CurrentStreakWeeks,
                    DaysRemaining = Math.Round(daysRemaining, 1),
                    RiskLevel = riskLevel,
                    Urgency = urgency
                });
            }

            return result.OrderBy(r => r.DaysRemaining).ToList();
        }

        // ── Engine 3: Milestone Detector ────────────────────────────

        /// <summary>
        /// Detects streak milestones for all customers.
        /// </summary>
        public List<StreakMilestone> DetectMilestones()
        {
            return DetectMilestones(CalculateStreaks());
        }

        private List<StreakMilestone> DetectMilestones(List<CustomerStreak> streaks)
        {
            var result = new List<StreakMilestone>();

            foreach (var streak in streaks)
            {
                if (streak.CurrentStreakWeeks < 4) continue;

                // Find the highest milestone they've reached
                foreach (var milestone in MilestoneThresholds)
                {
                    if (streak.CurrentStreakWeeks >= milestone.Value)
                    {
                        result.Add(new StreakMilestone
                        {
                            CustomerId = streak.CustomerId,
                            CustomerName = streak.CustomerName,
                            StreakWeeks = streak.CurrentStreakWeeks,
                            MilestoneName = string.Format("{0} ({1}+ weeks)", milestone.Key, milestone.Value),
                            Tier = milestone.Key
                        });
                        break; // Only report highest tier
                    }
                }
            }

            return result.OrderByDescending(m => m.StreakWeeks).ToList();
        }

        // ── Engine 4: Rescue Recommender ────────────────────────────

        /// <summary>
        /// Generates rescue recommendations for at-risk streaks.
        /// </summary>
        public List<RescueRecommendation> GenerateRescueRecommendations()
        {
            return GenerateRescueRecommendations(DetectAtRiskStreaks());
        }

        private List<RescueRecommendation> GenerateRescueRecommendations(List<AtRiskStreak> atRisk)
        {
            var allMovies = _movieRepo.GetAll();
            var allRentals = _rentalRepo.GetAll();
            var result = new List<RescueRecommendation>();

            foreach (var risk in atRisk)
            {
                var customerRentals = allRentals
                    .Where(r => r.CustomerId == risk.CustomerId)
                    .ToList();

                var rentedMovieIds = new HashSet<int>(customerRentals.Select(r => r.MovieId));

                // Find preferred genres
                var genreCounts = customerRentals
                    .Select(r => allMovies.FirstOrDefault(m => m.Id == r.MovieId))
                    .Where(m => m != null)
                    .GroupBy(m => m.Genre)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .ToList();

                // Recommend unwatched movies from preferred genres
                int count = 0;
                foreach (var genre in genreCounts)
                {
                    if (count >= _config.MaxRecommendationsPerCustomer) break;

                    var candidates = allMovies
                        .Where(m => m.Genre == genre && !rentedMovieIds.Contains(m.Id))
                        .ToList();

                    foreach (var movie in candidates)
                    {
                        if (count >= _config.MaxRecommendationsPerCustomer) break;

                        result.Add(new RescueRecommendation
                        {
                            CustomerId = risk.CustomerId,
                            CustomerName = risk.CustomerName,
                            MovieId = movie.Id,
                            MovieName = movie.Name,
                            Genre = movie.Genre.ToString(),
                            Reason = string.Format("Based on your love of {0} movies — rent to save your {1}-week streak!",
                                movie.Genre, risk.CurrentStreakWeeks),
                            StreakAtRisk = risk.CurrentStreakWeeks
                        });
                        count++;
                    }
                }
            }

            return result;
        }

        // ── Engine 5: Engagement Scorer ─────────────────────────────

        private double CalculateEngagementScore(
            int currentStreakWeeks,
            int totalActiveWeeks,
            int windowWeeks,
            List<Genre> genres,
            DateTime lastRental,
            DateTime now)
        {
            // Streak component (40%) — capped at 52 weeks
            double streakScore = Math.Min(currentStreakWeeks / 52.0, 1.0) * 40;

            // Frequency component (30%) — active weeks / window weeks
            double freqScore = Math.Min((double)totalActiveWeeks / windowWeeks, 1.0) * 30;

            // Diversity component (20%) — Shannon entropy of genres
            double diversityScore = 0;
            if (genres.Count > 0)
            {
                var genreDist = genres.GroupBy(g => g)
                    .Select(g => (double)g.Count() / genres.Count)
                    .ToList();
                double entropy = -genreDist.Sum(p => p > 0 ? p * Math.Log(p, 2) : 0);
                double maxEntropy = Math.Log(Math.Max(genreDist.Count, 2), 2);
                diversityScore = (maxEntropy > 0 ? entropy / maxEntropy : 0) * 20;
            }

            // Recency component (10%) — decay based on days since last rental
            double daysSinceLastRental = (now - lastRental).TotalDays;
            double recencyScore = Math.Max(0, 1.0 - daysSinceLastRental / 30.0) * 10;

            return Math.Round(Math.Min(streakScore + freqScore + diversityScore + recencyScore, 100), 1);
        }

        // ── Engine 6: Fleet Health Scorer ───────────────────────────

        /// <summary>
        /// Calculates store-wide streak health metrics.
        /// </summary>
        public FleetStreakHealth CalculateFleetHealth()
        {
            var streaks = CalculateStreaks();
            var atRisk = DetectAtRiskStreaks(streaks);
            return CalculateFleetHealth(streaks, atRisk);
        }

        private FleetStreakHealth CalculateFleetHealth(List<CustomerStreak> streaks, List<AtRiskStreak> atRisk)
        {
            int total = streaks.Count;
            if (total == 0)
            {
                return new FleetStreakHealth
                {
                    AverageEngagementScore = 0,
                    ActiveStreakPercentage = 0,
                    TotalCustomers = 0,
                    CustomersWithActiveStreaks = 0,
                    CustomersAtRisk = 0,
                    StreakDistribution = new Dictionary<string, int>(),
                    HealthScore = 0,
                    HealthTier = "Critical"
                };
            }

            int withActiveStreaks = streaks.Count(s => s.HasActiveStreak);
            double avgEngagement = streaks.Average(s => s.EngagementScore);
            double activePercentage = (double)withActiveStreaks / total * 100;

            // Distribution histogram
            var dist = new Dictionary<string, int>
            {
                { "0 weeks", streaks.Count(s => s.CurrentStreakWeeks == 0) },
                { "1-3 weeks", streaks.Count(s => s.CurrentStreakWeeks >= 1 && s.CurrentStreakWeeks <= 3) },
                { "4-7 weeks", streaks.Count(s => s.CurrentStreakWeeks >= 4 && s.CurrentStreakWeeks <= 7) },
                { "8-11 weeks", streaks.Count(s => s.CurrentStreakWeeks >= 8 && s.CurrentStreakWeeks <= 11) },
                { "12-25 weeks", streaks.Count(s => s.CurrentStreakWeeks >= 12 && s.CurrentStreakWeeks <= 25) },
                { "26-51 weeks", streaks.Count(s => s.CurrentStreakWeeks >= 26 && s.CurrentStreakWeeks <= 51) },
                { "52+ weeks", streaks.Count(s => s.CurrentStreakWeeks >= 52) }
            };

            // Composite health score
            double engagementComponent = avgEngagement * 0.4;
            double activeComponent = activePercentage * 0.4;
            double riskPenalty = total > 0 ? ((double)atRisk.Count / total) * 20 : 0;
            double healthScore = Math.Round(Math.Max(0, Math.Min(100,
                engagementComponent + activeComponent - riskPenalty)), 1);

            string tier;
            if (healthScore >= 80) tier = "Thriving";
            else if (healthScore >= 60) tier = "Healthy";
            else if (healthScore >= 40) tier = "Moderate";
            else if (healthScore >= 20) tier = "Concerning";
            else tier = "Critical";

            return new FleetStreakHealth
            {
                AverageEngagementScore = Math.Round(avgEngagement, 1),
                ActiveStreakPercentage = Math.Round(activePercentage, 1),
                TotalCustomers = total,
                CustomersWithActiveStreaks = withActiveStreaks,
                CustomersAtRisk = atRisk.Count,
                StreakDistribution = dist,
                HealthScore = healthScore,
                HealthTier = tier
            };
        }

        // ── Engine 7: Insight Generator ─────────────────────────────

        private List<string> GenerateInsights(
            List<CustomerStreak> streaks,
            List<AtRiskStreak> atRisk,
            List<StreakMilestone> milestones,
            FleetStreakHealth fleet)
        {
            var insights = new List<string>();

            // At-risk insights
            var highRisk = atRisk.Where(r => r.RiskLevel == "High").ToList();
            if (highRisk.Count > 0)
            {
                insights.Add(string.Format("⚠️ {0} customer{1} at HIGH risk of losing their streak today!",
                    highRisk.Count, highRisk.Count == 1 ? " is" : "s are"));
            }

            if (atRisk.Count > 0)
            {
                var totalWeeksAtRisk = atRisk.Sum(r => r.CurrentStreakWeeks);
                insights.Add(string.Format("🔥 {0} active streak{1} at risk, representing {2} combined streak weeks",
                    atRisk.Count, atRisk.Count == 1 ? "" : "s", totalWeeksAtRisk));
            }

            // Milestone insights
            if (milestones.Count > 0)
            {
                var topMilestone = milestones.First();
                insights.Add(string.Format("🏆 {0} achieved {1} milestone with a {2}-week streak!",
                    topMilestone.CustomerName, topMilestone.Tier, topMilestone.StreakWeeks));
            }

            var diamondCount = milestones.Count(m => m.Tier == "Diamond");
            if (diamondCount > 0)
            {
                insights.Add(string.Format("💎 {0} customer{1} reached Diamond status (52+ weeks)!",
                    diamondCount, diamondCount == 1 ? "" : "s"));
            }

            // Engagement insights
            var highEngagement = streaks.Count(s => s.EngagementScore >= 70);
            var lowEngagement = streaks.Count(s => s.EngagementScore < 20 && s.EngagementScore > 0);
            if (highEngagement > 0)
            {
                insights.Add(string.Format("🌟 {0} customer{1} with high engagement (70+ score)",
                    highEngagement, highEngagement == 1 ? "" : "s"));
            }
            if (lowEngagement > 0)
            {
                insights.Add(string.Format("📉 {0} customer{1} with low engagement — consider outreach",
                    lowEngagement, lowEngagement == 1 ? "" : "s"));
            }

            // Fleet health insight
            insights.Add(string.Format("📊 Store streak health: {0} ({1}/100)",
                fleet.HealthTier, fleet.HealthScore));

            // Longest streak insight
            var longestActive = streaks.OrderByDescending(s => s.CurrentStreakWeeks).FirstOrDefault();
            if (longestActive != null && longestActive.CurrentStreakWeeks > 0)
            {
                insights.Add(string.Format("👑 Longest active streak: {0} with {1} weeks",
                    longestActive.CustomerName, longestActive.CurrentStreakWeeks));
            }

            return insights;
        }

        // ── ISO Week Helpers ────────────────────────────────────────

        private static int GetIsoWeekNumber(DateTime date)
        {
            return CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(
                date, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        }

        private static int ISOWeekYear(DateTime date)
        {
            // Handle year boundary: a date in early Jan can be week 52/53 of prev year
            int week = GetIsoWeekNumber(date);
            if (week >= 52 && date.Month == 1) return date.Year - 1;
            if (week == 1 && date.Month == 12) return date.Year + 1;
            return date.Year;
        }

        private static bool AreConsecutiveWeeks(int year1, int week1, int year2, int week2)
        {
            if (year1 == year2) return week2 == week1 + 1;
            // Year boundary: last week of year1 -> week 1 of year2
            if (year2 == year1 + 1 && week2 == 1)
            {
                int maxWeek = GetIsoWeekNumber(new DateTime(year1, 12, 28));
                return week1 == maxWeek;
            }
            return false;
        }

        private static DateTime FirstDayOfIsoWeek(int year, int week)
        {
            // Jan 4 is always in week 1
            var jan4 = new DateTime(year, 1, 4);
            int dayOfWeek = ((int)jan4.DayOfWeek + 6) % 7; // Monday=0
            var firstMonday = jan4.AddDays(-dayOfWeek);
            return firstMonday.AddDays((week - 1) * 7);
        }

        private static DateTime EndOfIsoWeek(int year, int week)
        {
            return FirstDayOfIsoWeek(year, week).AddDays(7).AddSeconds(-1);
        }
    }
}
