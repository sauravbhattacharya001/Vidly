using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    // ----------------------------------------------------------------
    //  Revenue Attribution Engine — model classes
    // ----------------------------------------------------------------

    /// <summary>
    /// Full revenue attribution report produced by <see cref="Services.RevenueAttributionService"/>.
    /// </summary>
    public class RevenueAttributionReport
    {
        public DateTime GeneratedAt { get; set; }
        public decimal TotalRevenue { get; set; }
        public List<ChannelAttribution> ChannelBreakdown { get; set; }
        public List<TemporalAttribution> TemporalBreakdown { get; set; }
        public TierAttribution TierBreakdown { get; set; }
        public List<GenreRevenue> GenreBreakdown { get; set; }
        public List<PricingRuleImpact> PricingImpacts { get; set; }
        public RetentionAttribution RetentionBreakdown { get; set; }
        public List<string> Insights { get; set; }
        public double AttributionHealthScore { get; set; }
    }

    /// <summary>Revenue attributed to a logical channel (genre, new-release vs catalog, etc.).</summary>
    public class ChannelAttribution
    {
        public string Channel { get; set; }
        public decimal Revenue { get; set; }
        public double SharePercent { get; set; }
        public int RentalCount { get; set; }
        public decimal RevenuePerRental { get; set; }
    }

    /// <summary>Revenue attributed to a time period.</summary>
    public class TemporalAttribution
    {
        public string Period { get; set; }
        public decimal Revenue { get; set; }
        public double SharePercent { get; set; }
        public int RentalCount { get; set; }
        public double GrowthPercent { get; set; }
    }

    /// <summary>Revenue breakdown by membership tier with concentration index.</summary>
    public class TierAttribution
    {
        public List<TierRevenue> Tiers { get; set; }
        public double ConcentrationIndex { get; set; }
    }

    /// <summary>Revenue contribution of a single membership tier.</summary>
    public class TierRevenue
    {
        public string Tier { get; set; }
        public int CustomerCount { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal RevenuePerCapita { get; set; }
        public double SharePercent { get; set; }
    }

    /// <summary>Revenue metrics for a single genre.</summary>
    public class GenreRevenue
    {
        public string Genre { get; set; }
        public decimal Revenue { get; set; }
        public double SharePercent { get; set; }
        public int RentalCount { get; set; }
        public decimal RevenuePerRental { get; set; }
        public double GrowthPercent { get; set; }
        public string Trend { get; set; }
    }

    /// <summary>Estimated impact of a pricing rule type.</summary>
    public class PricingRuleImpact
    {
        public string RuleName { get; set; }
        public string RuleType { get; set; }
        public decimal EstimatedImpact { get; set; }
        public int AffectedRentals { get; set; }
        public decimal AverageEffect { get; set; }
    }

    /// <summary>Revenue split between new and returning customers.</summary>
    public class RetentionAttribution
    {
        public decimal NewCustomerRevenue { get; set; }
        public double NewCustomerShare { get; set; }
        public decimal ReturningCustomerRevenue { get; set; }
        public double ReturningCustomerShare { get; set; }
        public int NewCustomerCount { get; set; }
        public int ReturningCustomerCount { get; set; }
        public decimal RepeatRevenuePerCapita { get; set; }
        public double Top10PercentRevenueShare { get; set; }
    }
}
