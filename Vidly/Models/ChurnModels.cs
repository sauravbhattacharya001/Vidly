using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// Risk level for customer churn prediction.
    /// </summary>
    public enum ChurnRisk
    {
        Low = 1,
        Medium = 2,
        High = 3,
        Critical = 4
    }

    /// <summary>
    /// Individual churn risk assessment for a customer.
    /// </summary>
    public class ChurnProfile
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public MembershipType MembershipType { get; set; }

        /// <summary>Composite churn risk score (0-100, higher = more likely to churn).</summary>
        public double RiskScore { get; set; }

        /// <summary>Classified risk level based on score thresholds.</summary>
        public ChurnRisk RiskLevel { get; set; }

        /// <summary>Days since last rental activity.</summary>
        public int DaysSinceLastRental { get; set; }

        /// <summary>Total lifetime rentals.</summary>
        public int TotalRentals { get; set; }

        /// <summary>Total lifetime spend.</summary>
        public decimal TotalSpend { get; set; }

        /// <summary>Average days between rentals (lower = more engaged).</summary>
        public double AvgDaysBetweenRentals { get; set; }

        /// <summary>Trend in rental frequency: positive = increasing, negative = declining.</summary>
        public double FrequencyTrend { get; set; }

        /// <summary>Ratio of late returns to total returns (0-1).</summary>
        public double LateReturnRate { get; set; }

        /// <summary>Number of distinct genres rented (diversity indicator).</summary>
        public int GenreDiversity { get; set; }

        /// <summary>Actionable retention suggestions.</summary>
        public List<string> RetentionActions { get; set; } = new List<string>();

        /// <summary>Individual factor scores contributing to the composite risk.</summary>
        public ChurnFactors Factors { get; set; } = new ChurnFactors();
    }

    /// <summary>
    /// Individual factor scores that compose the overall churn risk.
    /// Each factor is 0-100 (higher = worse / more risk).
    /// </summary>
    public class ChurnFactors
    {
        /// <summary>Score based on inactivity duration (0-100).</summary>
        public double RecencyScore { get; set; }

        /// <summary>Score based on declining rental frequency (0-100).</summary>
        public double FrequencyDeclineScore { get; set; }

        /// <summary>Score based on low engagement / low total rentals (0-100).</summary>
        public double EngagementScore { get; set; }

        /// <summary>Score based on late return history (0-100).</summary>
        public double LateReturnScore { get; set; }

        /// <summary>Score based on low genre diversity (0-100).</summary>
        public double DiversityScore { get; set; }
    }

    /// <summary>
    /// Summary of churn risk across the entire customer base.
    /// </summary>
    public class ChurnSummary
    {
        public int TotalCustomersAnalyzed { get; set; }
        public int LowRiskCount { get; set; }
        public int MediumRiskCount { get; set; }
        public int HighRiskCount { get; set; }
        public int CriticalRiskCount { get; set; }

        /// <summary>Average risk score across all analyzed customers.</summary>
        public double AverageRiskScore { get; set; }

        /// <summary>Estimated revenue at risk from High + Critical customers.</summary>
        public decimal RevenueAtRisk { get; set; }

        /// <summary>Top N customers by risk score.</summary>
        public List<ChurnProfile> TopAtRisk { get; set; } = new List<ChurnProfile>();

        /// <summary>Distribution of churn risk by membership tier.</summary>
        public Dictionary<MembershipType, TierChurnStats> ByTier { get; set; }
            = new Dictionary<MembershipType, TierChurnStats>();
    }

    /// <summary>
    /// Churn statistics for a membership tier.
    /// </summary>
    public class TierChurnStats
    {
        public int Count { get; set; }
        public double AverageRiskScore { get; set; }
        public int HighRiskCount { get; set; }
    }

    /// <summary>
    /// Configuration for churn prediction weights and thresholds.
    /// </summary>
    public class ChurnConfig
    {
        /// <summary>Weight for recency factor (default 0.30).</summary>
        public double RecencyWeight { get; set; } = 0.30;

        /// <summary>Weight for frequency decline factor (default 0.25).</summary>
        public double FrequencyDeclineWeight { get; set; } = 0.25;

        /// <summary>Weight for engagement factor (default 0.20).</summary>
        public double EngagementWeight { get; set; } = 0.20;

        /// <summary>Weight for late return factor (default 0.15).</summary>
        public double LateReturnWeight { get; set; } = 0.15;

        /// <summary>Weight for diversity factor (default 0.10).</summary>
        public double DiversityWeight { get; set; } = 0.10;

        /// <summary>Days of inactivity that maps to maximum recency score.</summary>
        public int MaxInactiveDays { get; set; } = 180;

        /// <summary>Threshold score for Low risk (below this).</summary>
        public double LowThreshold { get; set; } = 25;

        /// <summary>Threshold score for Medium risk (below this).</summary>
        public double MediumThreshold { get; set; } = 50;

        /// <summary>Threshold score for High risk (below this).</summary>
        public double HighThreshold { get; set; } = 75;

        /// <summary>Minimum rentals required for trend analysis.</summary>
        public int MinRentalsForTrend { get; set; } = 3;

        /// <summary>Validates that weights sum to 1.0 (±0.01).</summary>
        public bool IsValid()
        {
            var sum = RecencyWeight + FrequencyDeclineWeight + EngagementWeight
                    + LateReturnWeight + DiversityWeight;
            return Math.Abs(sum - 1.0) < 0.01;
        }
    }
}
