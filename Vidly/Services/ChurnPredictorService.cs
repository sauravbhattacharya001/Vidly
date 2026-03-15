using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Predicts customer churn risk using multi-factor analysis of rental behavior.
    /// Combines recency, frequency trend, engagement depth, late return history,
    /// and genre diversity into a composite risk score with retention recommendations.
    /// </summary>
    public class ChurnPredictorService
    {
        private readonly ICustomerRepository _customerRepo;
        private readonly IRentalRepository _rentalRepo;
        private readonly IMovieRepository _movieRepo;
        private readonly ChurnConfig _config;

        public ChurnPredictorService(
            ICustomerRepository customerRepo,
            IRentalRepository rentalRepo,
            IMovieRepository movieRepo,
            ChurnConfig config = null)
        {
            _customerRepo = customerRepo
                ?? throw new ArgumentNullException(nameof(customerRepo));
            _rentalRepo = rentalRepo
                ?? throw new ArgumentNullException(nameof(rentalRepo));
            _movieRepo = movieRepo
                ?? throw new ArgumentNullException(nameof(movieRepo));
            _config = config ?? new ChurnConfig();

            if (!_config.IsValid())
                throw new ArgumentException("Churn config weights must sum to 1.0.");
        }

        // ── Individual Analysis ─────────────────────────────────────

        /// <summary>
        /// Analyze churn risk for a single customer.
        /// </summary>
        public ChurnProfile Analyze(int customerId, DateTime asOfDate)
        {
            var customer = _customerRepo.GetById(customerId);
            if (customer == null)
                throw new ArgumentException($"Customer {customerId} not found.");

            var rentals = _rentalRepo.GetAll()
                .Where(r => r.CustomerId == customerId)
                .OrderBy(r => r.RentalDate)
                .ToList();

            if (rentals.Count == 0)
            {
                return new ChurnProfile
                {
                    CustomerId = customer.Id,
                    CustomerName = customer.Name,
                    MembershipType = customer.MembershipType,
                    RiskScore = 100,
                    RiskLevel = ChurnRisk.Critical,
                    DaysSinceLastRental = int.MaxValue,
                    TotalRentals = 0,
                    TotalSpend = 0,
                    AvgDaysBetweenRentals = 0,
                    FrequencyTrend = 0,
                    LateReturnRate = 0,
                    GenreDiversity = 0,
                    Factors = new ChurnFactors
                    {
                        RecencyScore = 100,
                        FrequencyDeclineScore = 50,
                        EngagementScore = 80,
                        LateReturnScore = 0,
                        DiversityScore = 50
                    },
                    RetentionActions = new List<string> { "New customer — no rental history to analyze" }
                };
            }

            return BuildProfile(customer, rentals, asOfDate);
        }

        /// <summary>
        /// Analyze churn risk for all customers with rental history.
        /// </summary>
        public IReadOnlyList<ChurnProfile> AnalyzeAll(DateTime asOfDate)
        {
            var customers = _customerRepo.GetAll();
            var allRentals = _rentalRepo.GetAll();
            var rentalsByCustomer = allRentals
                .GroupBy(r => r.CustomerId)
                .ToDictionary(g => g.Key, g => g.OrderBy(r => r.RentalDate).ToList());

            var profiles = new List<ChurnProfile>();
            foreach (var c in customers)
            {
                if (!rentalsByCustomer.TryGetValue(c.Id, out var rentals) || rentals.Count == 0)
                    continue;
                profiles.Add(BuildProfile(c, rentals, asOfDate));
            }

            return profiles.OrderByDescending(p => p.RiskScore).ToList();
        }

        // ── Summary ─────────────────────────────────────────────────

        /// <summary>
        /// Generate a churn summary report for the entire customer base.
        /// </summary>
        public ChurnSummary GetSummary(DateTime asOfDate, int topN = 10)
        {
            var profiles = AnalyzeAll(asOfDate);
            if (profiles.Count == 0)
                return new ChurnSummary();

            var summary = new ChurnSummary
            {
                TotalCustomersAnalyzed = profiles.Count,
                LowRiskCount = profiles.Count(p => p.RiskLevel == ChurnRisk.Low),
                MediumRiskCount = profiles.Count(p => p.RiskLevel == ChurnRisk.Medium),
                HighRiskCount = profiles.Count(p => p.RiskLevel == ChurnRisk.High),
                CriticalRiskCount = profiles.Count(p => p.RiskLevel == ChurnRisk.Critical),
                AverageRiskScore = Math.Round(profiles.Average(p => p.RiskScore), 2),
                TopAtRisk = profiles.Take(topN).ToList(),
                RevenueAtRisk = profiles
                    .Where(p => p.RiskLevel >= ChurnRisk.High)
                    .Sum(p => p.TotalSpend)
            };

            // Per-tier breakdown
            foreach (var tier in profiles.GroupBy(p => p.MembershipType))
            {
                summary.ByTier[tier.Key] = new TierChurnStats
                {
                    Count = tier.Count(),
                    AverageRiskScore = Math.Round(tier.Average(p => p.RiskScore), 2),
                    HighRiskCount = tier.Count(p => p.RiskLevel >= ChurnRisk.High)
                };
            }

            return summary;
        }

        // ── Retention Targeting ─────────────────────────────────────

        /// <summary>
        /// Returns customers in a specific risk level, sorted by risk score descending.
        /// </summary>
        public IReadOnlyList<ChurnProfile> GetByRiskLevel(ChurnRisk level, DateTime asOfDate)
        {
            return AnalyzeAll(asOfDate)
                .Where(p => p.RiskLevel == level)
                .ToList();
        }

        /// <summary>
        /// Returns customers whose risk score exceeds a threshold.
        /// Useful for targeting retention campaigns.
        /// </summary>
        public IReadOnlyList<ChurnProfile> GetAboveThreshold(double threshold, DateTime asOfDate)
        {
            if (threshold < 0 || threshold > 100)
                throw new ArgumentOutOfRangeException(nameof(threshold), "Threshold must be 0-100.");

            return AnalyzeAll(asOfDate)
                .Where(p => p.RiskScore >= threshold)
                .ToList();
        }

        /// <summary>
        /// Identifies "winnable" customers — medium risk with high past engagement.
        /// These are the best candidates for retention efforts.
        /// </summary>
        public IReadOnlyList<ChurnProfile> GetWinnableCustomers(DateTime asOfDate, int minLifetimeRentals = 5)
        {
            return AnalyzeAll(asOfDate)
                .Where(p => p.RiskLevel == ChurnRisk.Medium || p.RiskLevel == ChurnRisk.High)
                .Where(p => p.TotalRentals >= minLifetimeRentals)
                .OrderByDescending(p => p.TotalSpend)
                .ToList();
        }

        // ── Comparison ──────────────────────────────────────────────

        /// <summary>
        /// Compare churn risk between two dates to detect improving/worsening customers.
        /// Returns (improved, worsened, stable) customer lists.
        /// </summary>
        public (IReadOnlyList<ChurnMovement> Improved,
                IReadOnlyList<ChurnMovement> Worsened,
                IReadOnlyList<ChurnMovement> Stable)
            CompareOverTime(DateTime earlier, DateTime later, double significantChange = 10.0)
        {
            var before = AnalyzeAll(earlier).ToDictionary(p => p.CustomerId);
            var after = AnalyzeAll(later).ToDictionary(p => p.CustomerId);

            var improved = new List<ChurnMovement>();
            var worsened = new List<ChurnMovement>();
            var stable = new List<ChurnMovement>();

            foreach (var kvp in after)
            {
                if (!before.TryGetValue(kvp.Key, out var prev)) continue;

                var delta = kvp.Value.RiskScore - prev.RiskScore;
                var movement = new ChurnMovement
                {
                    CustomerId = kvp.Key,
                    CustomerName = kvp.Value.CustomerName,
                    PreviousScore = prev.RiskScore,
                    CurrentScore = kvp.Value.RiskScore,
                    Delta = Math.Round(delta, 2),
                    PreviousLevel = prev.RiskLevel,
                    CurrentLevel = kvp.Value.RiskLevel
                };

                if (delta <= -significantChange)
                    improved.Add(movement);
                else if (delta >= significantChange)
                    worsened.Add(movement);
                else
                    stable.Add(movement);
            }

            return (
                improved.OrderBy(m => m.Delta).ToList(),
                worsened.OrderByDescending(m => m.Delta).ToList(),
                stable.OrderBy(m => Math.Abs(m.Delta)).ToList()
            );
        }

        // ── Private Helpers ─────────────────────────────────────────

        private ChurnProfile BuildProfile(Customer customer, List<Rental> rentals, DateTime asOfDate)
        {
            var movies = _movieRepo.GetAll().ToDictionary(m => m.Id);

            // Basic metrics
            var lastRental = rentals.Max(r => r.RentalDate);
            var daysSinceLast = Math.Max(0, (int)(asOfDate - lastRental).TotalDays);
            var totalSpend = rentals.Sum(r => r.TotalCost);

            // Late return rate
            var returned = rentals.Where(r => r.ReturnDate.HasValue).ToList();
            var lateCount = returned.Count(r => r.ReturnDate.Value > r.DueDate);
            var lateRate = returned.Count > 0 ? (double)lateCount / returned.Count : 0;

            // Genre diversity
            var genreIds = rentals
                .Where(r => movies.ContainsKey(r.MovieId) && movies[r.MovieId].Genre.HasValue)
                .Select(r => movies[r.MovieId].Genre.Value)
                .Distinct()
                .Count();

            // Average days between rentals
            var avgGap = CalculateAverageGap(rentals);

            // Frequency trend (compare first-half to second-half gap)
            var trend = CalculateFrequencyTrend(rentals);

            // Factor scores
            var factors = new ChurnFactors
            {
                RecencyScore = ScoreRecency(daysSinceLast),
                FrequencyDeclineScore = ScoreFrequencyDecline(trend, rentals.Count),
                EngagementScore = ScoreEngagement(rentals.Count),
                LateReturnScore = ScoreLateReturns(lateRate),
                DiversityScore = ScoreDiversity(genreIds, rentals.Count)
            };

            // Composite score
            var riskScore = Math.Round(
                factors.RecencyScore * _config.RecencyWeight +
                factors.FrequencyDeclineScore * _config.FrequencyDeclineWeight +
                factors.EngagementScore * _config.EngagementWeight +
                factors.LateReturnScore * _config.LateReturnWeight +
                factors.DiversityScore * _config.DiversityWeight,
                2);

            riskScore = Math.Min(100, Math.Max(0, riskScore));

            var riskLevel = ClassifyRisk(riskScore);

            var profile = new ChurnProfile
            {
                CustomerId = customer.Id,
                CustomerName = customer.Name,
                MembershipType = customer.MembershipType,
                RiskScore = riskScore,
                RiskLevel = riskLevel,
                DaysSinceLastRental = daysSinceLast,
                TotalRentals = rentals.Count,
                TotalSpend = totalSpend,
                AvgDaysBetweenRentals = Math.Round(avgGap, 2),
                FrequencyTrend = Math.Round(trend, 4),
                LateReturnRate = Math.Round(lateRate, 4),
                GenreDiversity = genreIds,
                Factors = factors
            };

            profile.RetentionActions = GenerateRetentionActions(profile);
            return profile;
        }

        private double ScoreRecency(int daysSinceLast)
        {
            // Linear scale: 0 days = 0 risk, MaxInactiveDays+ = 100 risk
            return Math.Min(100, (double)daysSinceLast / _config.MaxInactiveDays * 100);
        }

        private double ScoreFrequencyDecline(double trend, int totalRentals)
        {
            if (totalRentals < _config.MinRentalsForTrend)
                return 50; // Not enough data, neutral score

            // Negative trend = increasing gaps = declining frequency = higher risk
            // trend > 0 means gaps are growing (bad)
            if (trend <= 0) return Math.Max(0, 30 + trend * 10); // improving
            return Math.Min(100, 50 + trend * 25); // declining
        }

        private double ScoreEngagement(int totalRentals)
        {
            // Fewer rentals = higher churn risk
            if (totalRentals >= 20) return 0;
            if (totalRentals >= 10) return 20;
            if (totalRentals >= 5) return 40;
            if (totalRentals >= 3) return 60;
            return 80;
        }

        private double ScoreLateReturns(double lateRate)
        {
            // Higher late rate = dissatisfied customer = higher churn risk
            return Math.Min(100, lateRate * 100);
        }

        private double ScoreDiversity(int genreCount, int totalRentals)
        {
            if (totalRentals <= 1) return 50; // Not enough data
            // Low diversity with many rentals = niche customer, moderate risk
            // High diversity = exploratory, lower churn risk
            if (genreCount >= 5) return 10;
            if (genreCount >= 3) return 30;
            if (genreCount >= 2) return 50;
            return 70; // Single genre
        }

        private ChurnRisk ClassifyRisk(double score)
        {
            if (score < _config.LowThreshold) return ChurnRisk.Low;
            if (score < _config.MediumThreshold) return ChurnRisk.Medium;
            if (score < _config.HighThreshold) return ChurnRisk.High;
            return ChurnRisk.Critical;
        }

        private static List<double> ComputeRentalGaps(List<Rental> rentals)
        {
            if (rentals.Count < 2) return new List<double>();
            var dates = rentals.Select(r => r.RentalDate).OrderBy(d => d).ToList();
            var gaps = new List<double>(dates.Count - 1);
            for (int i = 1; i < dates.Count; i++)
                gaps.Add((dates[i] - dates[i - 1]).TotalDays);
            return gaps;
        }

        private double CalculateAverageGap(List<Rental> rentals)
        {
            var gaps = ComputeRentalGaps(rentals);
            return gaps.Count > 0 ? gaps.Average() : 0;
        }

        /// <summary>
        /// Compares average gap in first half vs second half of rental history.
        /// Positive = gaps growing (declining engagement). Negative = gaps shrinking (improving).
        /// </summary>
        private double CalculateFrequencyTrend(List<Rental> rentals)
        {
            if (rentals.Count < _config.MinRentalsForTrend) return 0;

            var gaps = ComputeRentalGaps(rentals);
            if (gaps.Count < 2) return 0;

            var mid = gaps.Count / 2;
            var firstHalf = gaps.Take(mid).Average();
            var secondHalf = gaps.Skip(mid).Average();

            // Normalize: positive means gaps are increasing (bad)
            if (firstHalf == 0) return 0;
            return (secondHalf - firstHalf) / firstHalf;
        }

        private List<string> GenerateRetentionActions(ChurnProfile profile)
        {
            var actions = new List<string>();

            // Recency-based actions
            if (profile.DaysSinceLastRental > 90)
                actions.Add("Send 'We miss you' email with personalized movie recommendations.");
            else if (profile.DaysSinceLastRental > 60)
                actions.Add("Send re-engagement email with new arrivals in preferred genres.");
            else if (profile.DaysSinceLastRental > 30)
                actions.Add("Send gentle reminder about new releases.");

            // Frequency decline
            if (profile.FrequencyTrend > 0.5)
                actions.Add("Offer limited-time rental discount to reverse declining visit frequency.");

            // Late return issues
            if (profile.LateReturnRate > 0.3)
                actions.Add("Consider extending rental periods or offering auto-renewal to reduce late returns.");

            // Low diversity — might be bored
            if (profile.GenreDiversity <= 1 && profile.TotalRentals >= 3)
                actions.Add("Suggest cross-genre recommendations to broaden interest.");

            // High-value at-risk
            if (profile.TotalSpend > 100 && profile.RiskLevel >= ChurnRisk.High)
                actions.Add("Assign to personal outreach program — high-value customer at risk.");

            // Membership upgrade opportunity
            if (profile.MembershipType == MembershipType.Basic && profile.TotalRentals >= 10)
                actions.Add("Offer complimentary Silver membership upgrade as retention incentive.");

            // Low engagement
            if (profile.TotalRentals <= 2)
                actions.Add("Trigger new-member onboarding sequence with curated picks.");

            if (actions.Count == 0)
                actions.Add("No immediate action needed — continue monitoring.");

            return actions;
        }
    }

    /// <summary>
    /// Represents a change in churn risk between two time periods.
    /// </summary>
    public class ChurnMovement
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public double PreviousScore { get; set; }
        public double CurrentScore { get; set; }
        public double Delta { get; set; }
        public ChurnRisk PreviousLevel { get; set; }
        public ChurnRisk CurrentLevel { get; set; }
    }
}
