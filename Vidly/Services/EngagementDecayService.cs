using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Autonomous Customer Engagement Decay Engine — monitors customer engagement
    /// levels over time, detects decay patterns, predicts re-engagement windows,
    /// and generates proactive intervention recommendations.
    ///
    /// 7 engines:
    /// 1. Engagement Score Calculator - exponential decay scoring 0-100
    /// 2. Phase Classifier - Active/Cooling/Dormant/AtRisk/Churned
    /// 3. Decay Rate Analyzer - per-customer decay velocity
    /// 4. Re-engagement Window Predictor - optimal outreach timing
    /// 5. Intervention Generator - prioritized recommendations
    /// 6. Fleet Health Scorer - aggregate health metrics
    /// 7. Insight Generator - natural-language observations
    /// </summary>
    public class EngagementDecayService
    {
        private readonly IRentalRepository _rentalRepo;
        private readonly ICustomerRepository _customerRepo;
        private readonly IMovieRepository _movieRepo;
        private readonly IClock _clock;

        // Phase thresholds (days since last rental)
        private const int ActiveThreshold = 14;
        private const int CoolingThreshold = 30;
        private const int DormantThreshold = 60;
        private const int AtRiskThreshold = 90;

        // Default decay lambda when no history (assumes ~14 day cadence)
        private const double DefaultLambda = 0.05;

        public EngagementDecayService(
            IRentalRepository rentalRepo,
            ICustomerRepository customerRepo,
            IMovieRepository movieRepo,
            IClock clock)
        {
            if (rentalRepo == null) throw new ArgumentNullException("rentalRepo");
            if (customerRepo == null) throw new ArgumentNullException("customerRepo");
            if (movieRepo == null) throw new ArgumentNullException("movieRepo");
            if (clock == null) throw new ArgumentNullException("clock");
            _rentalRepo = rentalRepo;
            _customerRepo = customerRepo;
            _movieRepo = movieRepo;
            _clock = clock;
        }

        // ----------------------------------------------------------------
        //  Public API
        // ----------------------------------------------------------------

        /// <summary>Generate a full engagement decay report.</summary>
        public EngagementDecayReport GenerateReport()
        {
            var profiles = BuildAllProfiles();
            var windows = PredictReengagementWindows(profiles);
            var interventions = GenerateInterventions(profiles);
            var fleet = ComputeFleetHealth(profiles);
            var trends = BuildTrendHistory(profiles);
            var insights = GenerateInsights(profiles, fleet);

            var score = fleet.OverallHealthScore;

            return new EngagementDecayReport
            {
                GeneratedAt = _clock.Now,
                FleetHealth = fleet,
                Profiles = profiles,
                Windows = windows,
                Interventions = interventions,
                TrendHistory = trends,
                Insights = insights,
                EngagementDecayScore = score
            };
        }

        /// <summary>Get a single customer's engagement profile.</summary>
        public CustomerEngagementProfile GetProfile(int customerId)
        {
            var customers = _customerRepo.GetAll();
            var customer = customers.FirstOrDefault(c => c.Id == customerId);
            if (customer == null) return null;

            var rentals = _rentalRepo.GetAll();
            var movies = _movieRepo.GetAll().ToDictionary(m => m.Id, m => m);
            return BuildProfile(customer, rentals, movies);
        }

        /// <summary>Get all re-engagement windows.</summary>
        public List<ReengagementWindow> GetReengagementWindows()
        {
            var profiles = BuildAllProfiles();
            return PredictReengagementWindows(profiles);
        }

        /// <summary>Get all interventions, priority-sorted.</summary>
        public List<EngagementIntervention> GetInterventions()
        {
            var profiles = BuildAllProfiles();
            return GenerateInterventions(profiles);
        }

        /// <summary>Get fleet health metrics.</summary>
        public EngagementFleetHealth GetFleetHealth()
        {
            var profiles = BuildAllProfiles();
            return ComputeFleetHealth(profiles);
        }

        // ----------------------------------------------------------------
        //  Engine 1: Engagement Score Calculator
        // ----------------------------------------------------------------

        private double CalculateEngagementScore(int daysSinceLastRental, double lambda, int rentalsLast30)
        {
            if (daysSinceLastRental < 0) daysSinceLastRental = 0;

            // Base score from exponential decay
            double baseScore = 100.0 * Math.Exp(-lambda * daysSinceLastRental);

            // Frequency bonus: up to 20 points for activity in last 30 days
            double frequencyBonus = Math.Min(20.0, rentalsLast30 * 5.0);

            double total = baseScore + frequencyBonus;
            return Math.Round(Math.Min(100.0, Math.Max(0.0, total)), 1);
        }

        // ----------------------------------------------------------------
        //  Engine 2: Phase Classifier
        // ----------------------------------------------------------------

        private EngagementPhase ClassifyPhase(int daysSinceLastRental)
        {
            if (daysSinceLastRental <= ActiveThreshold) return EngagementPhase.Active;
            if (daysSinceLastRental <= CoolingThreshold) return EngagementPhase.Cooling;
            if (daysSinceLastRental <= DormantThreshold) return EngagementPhase.Dormant;
            if (daysSinceLastRental <= AtRiskThreshold) return EngagementPhase.AtRisk;
            return EngagementPhase.Churned;
        }

        // ----------------------------------------------------------------
        //  Engine 3: Decay Rate Analyzer
        // ----------------------------------------------------------------

        private double ComputeDecayRate(List<DateTime> rentalDates)
        {
            if (rentalDates.Count < 2) return DefaultLambda;

            var sorted = rentalDates.OrderBy(d => d).ToList();
            var intervals = new List<double>();
            for (int i = 1; i < sorted.Count; i++)
            {
                var days = (sorted[i] - sorted[i - 1]).TotalDays;
                if (days > 0) intervals.Add(days);
            }

            if (intervals.Count == 0) return DefaultLambda;

            // Lambda = ln(2) / median interval (half-life model)
            intervals.Sort();
            double median = intervals[intervals.Count / 2];
            if (median <= 0) return DefaultLambda;

            return Math.Round(Math.Log(2) / median, 4);
        }

        private double ComputeAverageInterval(List<DateTime> rentalDates)
        {
            if (rentalDates.Count < 2) return 0;

            var sorted = rentalDates.OrderBy(d => d).ToList();
            double totalDays = 0;
            int count = 0;
            for (int i = 1; i < sorted.Count; i++)
            {
                var days = (sorted[i] - sorted[i - 1]).TotalDays;
                if (days > 0)
                {
                    totalDays += days;
                    count++;
                }
            }

            return count > 0 ? Math.Round(totalDays / count, 1) : 0;
        }

        // ----------------------------------------------------------------
        //  Engine 4: Re-engagement Window Predictor
        // ----------------------------------------------------------------

        private List<ReengagementWindow> PredictReengagementWindows(List<CustomerEngagementProfile> profiles)
        {
            var windows = new List<ReengagementWindow>();
            var now = _clock.Now;

            foreach (var p in profiles)
            {
                // Only generate windows for Cooling/Dormant/AtRisk customers
                if (p.CurrentPhase == EngagementPhase.Active || p.CurrentPhase == EngagementPhase.Churned)
                    continue;

                if (p.AverageInterRentalDays <= 0 || !p.LastRentalDate.HasValue)
                    continue;

                // Predicted next rental = last rental + avg interval
                var predictedNext = p.LastRentalDate.Value.AddDays(p.AverageInterRentalDays);

                // Window: 2 days before to 3 days after predicted next
                var windowStart = predictedNext.AddDays(-2);
                var windowEnd = predictedNext.AddDays(3);

                // If window has fully passed, shift to NOW
                if (windowEnd < now)
                {
                    windowStart = now;
                    windowEnd = now.AddDays(3);
                }

                // Confidence based on phase (earlier = higher confidence)
                double confidence = p.CurrentPhase == EngagementPhase.Cooling ? 0.8
                    : p.CurrentPhase == EngagementPhase.Dormant ? 0.5
                    : 0.3;

                // Adjust confidence by frequency
                if (p.TotalRentals >= 10) confidence = Math.Min(1.0, confidence + 0.1);

                var intervention = p.CurrentPhase == EngagementPhase.Cooling
                    ? InterventionType.GenreReminder
                    : p.CurrentPhase == EngagementPhase.Dormant
                        ? InterventionType.PersonalizedPick
                        : InterventionType.WinBackOffer;

                windows.Add(new ReengagementWindow
                {
                    CustomerId = p.CustomerId,
                    CustomerName = p.CustomerName,
                    WindowStart = windowStart,
                    WindowEnd = windowEnd,
                    Confidence = Math.Round(confidence, 2),
                    Reason = string.Format("{0} typically rents every {1:F0} days; last rented {2} days ago",
                        p.CustomerName, p.AverageInterRentalDays, p.DaysSinceLastRental),
                    RecommendedIntervention = intervention
                });
            }

            return windows.OrderByDescending(w => w.Confidence).ToList();
        }

        // ----------------------------------------------------------------
        //  Engine 5: Intervention Generator
        // ----------------------------------------------------------------

        private List<EngagementIntervention> GenerateInterventions(List<CustomerEngagementProfile> profiles)
        {
            var interventions = new List<EngagementIntervention>();

            foreach (var p in profiles)
            {
                if (p.CurrentPhase == EngagementPhase.Active) continue;

                EngagementIntervention intervention = null;

                switch (p.CurrentPhase)
                {
                    case EngagementPhase.Cooling:
                        intervention = new EngagementIntervention
                        {
                            CustomerId = p.CustomerId,
                            CustomerName = p.CustomerName,
                            Type = !string.IsNullOrEmpty(p.PreferredGenre)
                                ? InterventionType.GenreReminder
                                : InterventionType.NewReleaseAlert,
                            Message = !string.IsNullOrEmpty(p.PreferredGenre)
                                ? string.Format("New {0} titles available — {1} might enjoy these!", p.PreferredGenre, p.CustomerName)
                                : string.Format("Check out this week's new releases, {0}!", p.CustomerName),
                            Priority = 40 + (p.DecayRate * 100),
                            ExpectedImpact = 0.7,
                            Rationale = "Customer in cooling phase; gentle genre-based reminder has high re-engagement probability"
                        };
                        break;

                    case EngagementPhase.Dormant:
                        intervention = new EngagementIntervention
                        {
                            CustomerId = p.CustomerId,
                            CustomerName = p.CustomerName,
                            Type = InterventionType.PersonalizedPick,
                            Message = string.Format("We picked something special for you, {0} — based on your taste!", p.CustomerName),
                            Priority = 60 + (p.DecayRate * 100),
                            ExpectedImpact = 0.5,
                            Rationale = string.Format("Dormant for {0} days; personalized recommendation needed to re-engage", p.DaysSinceLastRental)
                        };
                        break;

                    case EngagementPhase.AtRisk:
                        intervention = new EngagementIntervention
                        {
                            CustomerId = p.CustomerId,
                            CustomerName = p.CustomerName,
                            Type = InterventionType.LoyaltyBonus,
                            Message = string.Format("We miss you, {0}! Here's a loyalty bonus for your next rental.", p.CustomerName),
                            Priority = 80 + (p.DecayRate * 50),
                            ExpectedImpact = 0.4,
                            Rationale = string.Format("At-risk customer ({0} days inactive); loyalty incentive to prevent churn", p.DaysSinceLastRental)
                        };
                        break;

                    case EngagementPhase.Churned:
                        intervention = new EngagementIntervention
                        {
                            CustomerId = p.CustomerId,
                            CustomerName = p.CustomerName,
                            Type = InterventionType.WinBackOffer,
                            Message = string.Format("Come back, {0}! Your first rental back is on us.", p.CustomerName),
                            Priority = 90,
                            ExpectedImpact = 0.2,
                            Rationale = string.Format("Churned customer ({0} days inactive); aggressive win-back offer as last resort", p.DaysSinceLastRental)
                        };
                        break;
                }

                if (intervention != null)
                {
                    intervention.Priority = Math.Round(Math.Min(100, intervention.Priority), 1);
                    interventions.Add(intervention);
                }
            }

            return interventions.OrderByDescending(i => i.Priority).ToList();
        }

        // ----------------------------------------------------------------
        //  Engine 6: Fleet Health Scorer
        // ----------------------------------------------------------------

        private EngagementFleetHealth ComputeFleetHealth(List<CustomerEngagementProfile> profiles)
        {
            if (profiles.Count == 0)
            {
                return new EngagementFleetHealth
                {
                    OverallHealthScore = 0,
                    TotalCustomers = 0,
                    HealthTier = "Emergency",
                    Trend = "Stable"
                };
            }

            int active = profiles.Count(p => p.CurrentPhase == EngagementPhase.Active);
            int cooling = profiles.Count(p => p.CurrentPhase == EngagementPhase.Cooling);
            int dormant = profiles.Count(p => p.CurrentPhase == EngagementPhase.Dormant);
            int atRisk = profiles.Count(p => p.CurrentPhase == EngagementPhase.AtRisk);
            int churned = profiles.Count(p => p.CurrentPhase == EngagementPhase.Churned);
            int total = profiles.Count;

            double avgScore = profiles.Average(p => p.EngagementScore);
            double activePercentage = (double)active / total * 100;
            double churnRate = (double)churned / total * 100;

            // Weighted health: Active=100, Cooling=70, Dormant=40, AtRisk=20, Churned=0
            double weightedSum = active * 100.0 + cooling * 70.0 + dormant * 40.0 + atRisk * 20.0;
            double healthScore = Math.Round(weightedSum / total, 1);

            string tier;
            if (healthScore >= 80) tier = "Thriving";
            else if (healthScore >= 60) tier = "Healthy";
            else if (healthScore >= 40) tier = "Concerning";
            else if (healthScore >= 20) tier = "Critical";
            else tier = "Emergency";

            // Trend based on decay rates
            double avgDecay = profiles.Average(p => p.DecayRate);
            string trend = avgDecay < 0.04 ? "Improving"
                : avgDecay < 0.07 ? "Stable"
                : "Declining";

            return new EngagementFleetHealth
            {
                OverallHealthScore = healthScore,
                TotalCustomers = total,
                ActiveCount = active,
                CoolingCount = cooling,
                DormantCount = dormant,
                AtRiskCount = atRisk,
                ChurnedCount = churned,
                ActivePercentage = Math.Round(activePercentage, 1),
                ChurnRate = Math.Round(churnRate, 1),
                AverageEngagementScore = Math.Round(avgScore, 1),
                HealthTier = tier,
                Trend = trend
            };
        }

        // ----------------------------------------------------------------
        //  Engine 7: Insight Generator
        // ----------------------------------------------------------------

        private List<string> GenerateInsights(List<CustomerEngagementProfile> profiles, EngagementFleetHealth fleet)
        {
            var insights = new List<string>();

            if (profiles.Count == 0)
            {
                insights.Add("No customers found in the system.");
                return insights;
            }

            // Insight 1: Fleet summary
            insights.Add(string.Format("{0} of {1} customers ({2:F0}%) are actively engaged.",
                fleet.ActiveCount, fleet.TotalCustomers, fleet.ActivePercentage));

            // Insight 2: Churn alert
            if (fleet.ChurnedCount > 0)
            {
                insights.Add(string.Format("{0} customer{1} ha{2} churned (90+ days inactive) — win-back campaigns recommended.",
                    fleet.ChurnedCount,
                    fleet.ChurnedCount > 1 ? "s" : "",
                    fleet.ChurnedCount > 1 ? "ve" : "s"));
            }

            // Insight 3: At-risk alert
            if (fleet.AtRiskCount > 0)
            {
                insights.Add(string.Format("{0} customer{1} at risk of churning — immediate intervention recommended.",
                    fleet.AtRiskCount,
                    fleet.AtRiskCount > 1 ? "s are" : " is"));
            }

            // Insight 4: Genre-based decay comparison
            var genreGroups = profiles
                .Where(p => !string.IsNullOrEmpty(p.PreferredGenre))
                .GroupBy(p => p.PreferredGenre)
                .Where(g => g.Count() >= 2)
                .Select(g => new { Genre = g.Key, AvgDecay = g.Average(p => p.DecayRate) })
                .OrderByDescending(g => g.AvgDecay)
                .ToList();

            if (genreGroups.Count >= 2)
            {
                var fastest = genreGroups.First();
                var slowest = genreGroups.Last();
                if (fastest.AvgDecay > slowest.AvgDecay * 1.3)
                {
                    insights.Add(string.Format("{0} fans show faster engagement decay than {1} fans — consider targeted retention for {0} enthusiasts.",
                        fastest.Genre, slowest.Genre));
                }
            }

            // Insight 5: Upcoming phase transitions
            var nearTransitions = profiles
                .Where(p => !string.IsNullOrEmpty(p.PhaseTransitionWarning))
                .ToList();
            if (nearTransitions.Count > 0)
            {
                insights.Add(string.Format("{0} customer{1} approaching a phase transition in the next few days.",
                    nearTransitions.Count,
                    nearTransitions.Count > 1 ? "s are" : " is"));
            }

            // Insight 6: High-value at-risk
            var highValueAtRisk = profiles
                .Where(p => (p.CurrentPhase == EngagementPhase.AtRisk || p.CurrentPhase == EngagementPhase.Dormant)
                            && p.TotalRentals >= 10)
                .ToList();
            if (highValueAtRisk.Count > 0)
            {
                insights.Add(string.Format("{0} high-value customer{1} (10+ rentals) {2} disengaging — prioritize retention.",
                    highValueAtRisk.Count,
                    highValueAtRisk.Count > 1 ? "s" : "",
                    highValueAtRisk.Count > 1 ? "are" : "is"));
            }

            // Insight 7: Overall trend
            insights.Add(string.Format("Fleet engagement trend: {0}. Overall health: {1} ({2:F0}/100).",
                fleet.Trend, fleet.HealthTier, fleet.OverallHealthScore));

            return insights;
        }

        // ----------------------------------------------------------------
        //  Helpers
        // ----------------------------------------------------------------

        private List<CustomerEngagementProfile> BuildAllProfiles()
        {
            var customers = _customerRepo.GetAll();
            var rentals = _rentalRepo.GetAll();
            var movies = _movieRepo.GetAll().ToDictionary(m => m.Id, m => m);

            return customers.Select(c => BuildProfile(c, rentals, movies)).ToList();
        }

        private CustomerEngagementProfile BuildProfile(
            Customer customer,
            IReadOnlyList<Rental> allRentals,
            Dictionary<int, Movie> movieLookup)
        {
            var now = _clock.Now;
            var customerRentals = allRentals
                .Where(r => r.CustomerId == customer.Id)
                .OrderByDescending(r => r.RentalDate)
                .ToList();

            var lastRentalDate = customerRentals.FirstOrDefault()?.RentalDate;
            int daysSinceLast = lastRentalDate.HasValue
                ? Math.Max(0, (int)Math.Ceiling((now - lastRentalDate.Value).TotalDays))
                : 999;

            var rentalDates = customerRentals.Select(r => r.RentalDate).ToList();
            double lambda = ComputeDecayRate(rentalDates);
            double avgInterval = ComputeAverageInterval(rentalDates);

            int rentalsLast30 = customerRentals.Count(r => (now - r.RentalDate).TotalDays <= 30);
            int rentalsLast90 = customerRentals.Count(r => (now - r.RentalDate).TotalDays <= 90);

            double score = CalculateEngagementScore(daysSinceLast, lambda, rentalsLast30);
            var phase = ClassifyPhase(daysSinceLast);

            // Preferred genre
            string preferredGenre = null;
            if (customerRentals.Count > 0)
            {
                var genreCounts = customerRentals
                    .Where(r => movieLookup.ContainsKey(r.MovieId) && movieLookup[r.MovieId].Genre.HasValue)
                    .GroupBy(r => movieLookup[r.MovieId].Genre.Value)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault();
                if (genreCounts != null)
                    preferredGenre = genreCounts.Key.ToString();
            }

            // Predicted days to churn (from current position to 90-day mark)
            double predictedDaysToChurn = daysSinceLast >= AtRiskThreshold ? 0
                : AtRiskThreshold - daysSinceLast;

            // Predicted next rental date
            DateTime? predictedNext = null;
            if (avgInterval > 0 && lastRentalDate.HasValue)
            {
                predictedNext = lastRentalDate.Value.AddDays(avgInterval);
            }

            // Phase transition warning
            string warning = null;
            int daysToNextThreshold = 0;
            if (phase == EngagementPhase.Active)
            {
                daysToNextThreshold = ActiveThreshold - daysSinceLast;
                if (daysToNextThreshold <= 5)
                    warning = string.Format("Will transition to Cooling in {0} day{1}", daysToNextThreshold, daysToNextThreshold != 1 ? "s" : "");
            }
            else if (phase == EngagementPhase.Cooling)
            {
                daysToNextThreshold = CoolingThreshold - daysSinceLast;
                if (daysToNextThreshold <= 5)
                    warning = string.Format("Will transition to Dormant in {0} day{1}", daysToNextThreshold, daysToNextThreshold != 1 ? "s" : "");
            }
            else if (phase == EngagementPhase.Dormant)
            {
                daysToNextThreshold = DormantThreshold - daysSinceLast;
                if (daysToNextThreshold <= 5)
                    warning = string.Format("Will transition to AtRisk in {0} day{1}", daysToNextThreshold, daysToNextThreshold != 1 ? "s" : "");
            }
            else if (phase == EngagementPhase.AtRisk)
            {
                daysToNextThreshold = AtRiskThreshold - daysSinceLast;
                if (daysToNextThreshold <= 5)
                    warning = string.Format("Will transition to Churned in {0} day{1}", daysToNextThreshold, daysToNextThreshold != 1 ? "s" : "");
            }

            return new CustomerEngagementProfile
            {
                CustomerId = customer.Id,
                CustomerName = customer.Name,
                CurrentPhase = phase,
                EngagementScore = score,
                DecayRate = lambda,
                DaysSinceLastRental = daysSinceLast,
                TotalRentals = customerRentals.Count,
                RentalsLast30Days = rentalsLast30,
                RentalsLast90Days = rentalsLast90,
                AverageInterRentalDays = avgInterval,
                PredictedDaysToChurn = predictedDaysToChurn,
                PreferredGenre = preferredGenre,
                LastRentalDate = lastRentalDate,
                PredictedNextRentalDate = predictedNext,
                PhaseTransitionWarning = warning
            };
        }

        private List<EngagementTrendPoint> BuildTrendHistory(List<CustomerEngagementProfile> profiles)
        {
            // Generate simulated trend points for last 4 weeks
            var now = _clock.Now;
            var points = new List<EngagementTrendPoint>();

            for (int weeksAgo = 4; weeksAgo >= 0; weeksAgo--)
            {
                var date = now.AddDays(-weeksAgo * 7);
                // Approximate: shift days-since by the week offset
                int activeCount = 0;
                int churnedCount = 0;
                double totalScore = 0;

                foreach (var p in profiles)
                {
                    int adjustedDays = Math.Max(0, p.DaysSinceLastRental - (weeksAgo * 7));
                    var phase = ClassifyPhase(adjustedDays);
                    double score = CalculateEngagementScore(adjustedDays, p.DecayRate, p.RentalsLast30Days);
                    totalScore += score;
                    if (phase == EngagementPhase.Active) activeCount++;
                    if (phase == EngagementPhase.Churned) churnedCount++;
                }

                points.Add(new EngagementTrendPoint
                {
                    Date = date.Date,
                    AverageScore = profiles.Count > 0 ? Math.Round(totalScore / profiles.Count, 1) : 0,
                    ActiveCount = activeCount,
                    ChurnedCount = churnedCount
                });
            }

            return points;
        }
    }
}
