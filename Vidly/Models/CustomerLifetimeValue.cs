using System.Collections.Generic;

namespace Vidly.Models
{
    public class ClvSummary
    {
        public decimal TotalEstimatedClv { get; set; }
        public decimal AverageClv { get; set; }
        public decimal MedianClv { get; set; }
        public int TotalCustomers { get; set; }
        public int WhaleCount { get; set; }
        public int HighValueCount { get; set; }
        public int MidValueCount { get; set; }
        public int AtRiskCount { get; set; }
        public List<ClvCustomerProfile> TopCustomers { get; set; }
        public List<ClvCustomerProfile> AtRiskCustomers { get; set; }
        public List<ClvRecommendation> Recommendations { get; set; }
        public List<ClvTierBreakdown> TierBreakdown { get; set; }
        public ClvTrend Trend { get; set; }
    }

    public class ClvCustomerProfile
    {
        public int CustomerId { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public MembershipType Membership { get; set; }
        public decimal EstimatedClv { get; set; }
        public decimal HistoricalRevenue { get; set; }
        public decimal PredictedFutureRevenue { get; set; }
        public int TotalRentals { get; set; }
        public decimal AvgMonthlySpend { get; set; }
        public int MonthsActive { get; set; }
        public string Tier { get; set; }
        public string Trajectory { get; set; }
        public decimal RetentionProbability { get; set; }
        public List<string> ValueDrivers { get; set; }
    }

    public class ClvTierBreakdown
    {
        public string Tier { get; set; }
        public int Count { get; set; }
        public decimal TotalClv { get; set; }
        public decimal AvgClv { get; set; }
        public decimal RevenueShare { get; set; }
        public string Color { get; set; }
    }

    public class ClvRecommendation
    {
        public string Type { get; set; }
        public string Priority { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int AffectedCustomers { get; set; }
        public decimal EstimatedImpact { get; set; }
    }

    public class ClvTrend
    {
        public List<string> Labels { get; set; }
        public List<decimal> AvgClvOverTime { get; set; }
        public List<decimal> NewCustomerClv { get; set; }
    }
}
