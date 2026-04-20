using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    public enum PricingRuleType
    {
        DemandSurge,
        OffPeakDiscount,
        NewReleasePremium,
        LoyaltyDiscount,
        BundleDiscount,
        SeasonalAdjustment
    }

    public enum DemandTrend
    {
        Rising,
        Stable,
        Falling
    }

    public class PricingRule
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public PricingRuleType Type { get; set; }
        public decimal Multiplier { get; set; }
        public bool IsActive { get; set; }
        public int Priority { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class PricingRecommendation
    {
        public int MovieId { get; set; }
        public string MovieTitle { get; set; }
        public decimal CurrentPrice { get; set; }
        public decimal RecommendedPrice { get; set; }
        public string Reason { get; set; }
        public int Confidence { get; set; }
        public PricingRuleType RuleType { get; set; }
        public decimal PotentialRevenueDelta { get; set; }
    }

    public class PricingSnapshot
    {
        public DateTime Date { get; set; }
        public decimal AvgPrice { get; set; }
        public int TotalRentals { get; set; }
        public decimal Revenue { get; set; }
        public int AdjustmentsApplied { get; set; }
    }

    public class PricingStats
    {
        public decimal AvgMultiplier { get; set; }
        public int TotalRecommendations { get; set; }
        public decimal EstimatedRevenueGain { get; set; }
        public int RulesActive { get; set; }
        public bool AutopilotEnabled { get; set; }
    }

    public class DemandEntry
    {
        public int MovieId { get; set; }
        public string MovieTitle { get; set; }
        public int RentalCount { get; set; }
        public double DemandScore { get; set; }
        public DemandTrend Trend { get; set; }
    }

    public class PricingDashboard
    {
        public List<PricingRule> ActiveRules { get; set; }
        public List<PricingRecommendation> Recommendations { get; set; }
        public List<PricingSnapshot> Snapshots { get; set; }
        public Dictionary<DayOfWeek, List<double>> DemandHeatmap { get; set; }
        public List<DemandEntry> TopDemandMovies { get; set; }
        public PricingStats Stats { get; set; }
    }
}
