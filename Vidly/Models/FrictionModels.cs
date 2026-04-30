using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    // ── Friction Detector Enums ──────────────────────────────────────

    /// <summary>
    /// Categories of friction detected in the rental journey.
    /// </summary>
    public enum FrictionCategory
    {
        Availability = 1,
        Pricing = 2,
        Overdue = 3,
        Frequency = 4,
        GenreLock = 5,
        ReturnDelay = 6,
        NewCustomerDrop = 7,
        HighCostAbandonment = 8
    }

    /// <summary>
    /// Severity level of a friction point.
    /// </summary>
    public enum FrictionSeverity
    {
        Low = 1,
        Medium = 2,
        High = 3,
        Critical = 4
    }

    /// <summary>
    /// Type of recommendation to reduce friction.
    /// </summary>
    public enum RecommendationType
    {
        Discount = 1,
        Reminder = 2,
        GenreSuggestion = 3,
        LoyaltyReward = 4,
        ExtendedDueDate = 5,
        PersonalOutreach = 6,
        BundleDeal = 7,
        GracePeriod = 8
    }

    // ── Friction Detector Models ─────────────────────────────────────

    /// <summary>
    /// A single detected friction point for a customer.
    /// </summary>
    public class FrictionPoint
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public FrictionCategory Category { get; set; }
        public FrictionSeverity Severity { get; set; }
        public double Score { get; set; }
        public string Description { get; set; }
        public string Evidence { get; set; }
        public DateTime DetectedAt { get; set; }
    }

    /// <summary>
    /// A recommendation to reduce friction.
    /// </summary>
    public class FrictionRecommendation
    {
        public FrictionCategory TargetFriction { get; set; }
        public RecommendationType Type { get; set; }
        public string Action { get; set; }
        public double ExpectedImpact { get; set; }
        public string Rationale { get; set; }
    }

    /// <summary>
    /// Friction profile for a single customer.
    /// </summary>
    public class CustomerFrictionProfile
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public double OverallFrictionScore { get; set; }
        public string RiskLevel { get; set; }
        public List<FrictionPoint> FrictionPoints { get; set; }
        public List<FrictionRecommendation> Recommendations { get; set; }
        public int TotalRentals { get; set; }
        public int DaysSinceLastRental { get; set; }
        public double AvgDaysOverdue { get; set; }
    }

    /// <summary>
    /// Store-wide friction distribution by category.
    /// </summary>
    public class FrictionHeatmap
    {
        public Dictionary<FrictionCategory, int> CategoryCounts { get; set; }
        public Dictionary<FrictionCategory, double> CategoryAvgSeverity { get; set; }
        public FrictionCategory TopFrictionSource { get; set; }
        public double StoreWideFrictionIndex { get; set; }
    }

    /// <summary>
    /// Friction trend for a time period.
    /// </summary>
    public class FrictionTrend
    {
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public double FrictionIndex { get; set; }
        public int AffectedCustomers { get; set; }
        public FrictionCategory DominantCategory { get; set; }
    }

    /// <summary>
    /// Full friction analysis report.
    /// </summary>
    public class FrictionReport
    {
        public DateTime GeneratedAt { get; set; }
        public int TotalCustomersAnalyzed { get; set; }
        public int CustomersWithFriction { get; set; }
        public double StoreHealthScore { get; set; }
        public FrictionHeatmap Heatmap { get; set; }
        public List<CustomerFrictionProfile> TopFrictionCustomers { get; set; }
        public List<FrictionTrend> Trends { get; set; }
        public List<FrictionRecommendation> StoreWideRecommendations { get; set; }
        public Dictionary<FrictionCategory, string> Insights { get; set; }
    }
}
