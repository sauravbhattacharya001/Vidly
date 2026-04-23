using System.Collections.Generic;

namespace Vidly.Models
{
    // ── Customer Segmentation Models ────────────────────────────────

    /// <summary>
    /// RFM-based customer segment profile with migration tracking
    /// and proactive campaign recommendations.
    /// </summary>
    public class CustomerSegment
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int TotalRentals { get; set; }
        public decimal TotalSpend { get; set; }
        public double Recency { get; set; }
        public double Frequency { get; set; }
        public double Monetary { get; set; }
        public double RfmScore { get; set; }
        public string Segment { get; set; }
        public string PreviousSegment { get; set; }
        public string MigrationDirection { get; set; }
        public List<string> CampaignRecommendations { get; set; }
    }

    /// <summary>Per-segment aggregate statistics.</summary>
    public class SegmentSummary
    {
        public string Segment { get; set; }
        public int Count { get; set; }
        public double Percentage { get; set; }
        public double AvgRfmScore { get; set; }
        public double AvgRecency { get; set; }
        public double AvgFrequency { get; set; }
        public double AvgMonetary { get; set; }
        public string Color { get; set; }
    }

    /// <summary>Tracks customer movement between segments.</summary>
    public class MigrationFlow
    {
        public string FromSegment { get; set; }
        public string ToSegment { get; set; }
        public int Count { get; set; }
    }

    /// <summary>Fleet-level segmentation analysis.</summary>
    public class SegmentationFleet
    {
        public List<CustomerSegment> Customers { get; set; }
        public List<SegmentSummary> Summaries { get; set; }
        public List<MigrationFlow> Migrations { get; set; }
        public int TotalCustomers { get; set; }
        public double OverallHealthScore { get; set; }
        public List<string> ProactiveInsights { get; set; }
        public string GeneratedAt { get; set; }
    }
}
