using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// Comprehensive analytics for a single movie: rental summary, revenue,
    /// customer demographics, monthly trends, and performance score.
    /// </summary>
    public class MovieInsight
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public Genre? Genre { get; set; }
        public int? Rating { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public RentalSummary RentalSummary { get; set; }
        public RevenueBreakdown Revenue { get; set; }
        public CustomerDemographicBreakdown CustomerDemographics { get; set; }
        public List<MonthlyRentalPoint> MonthlyTrend { get; set; }
        public PerformanceScore PerformanceScore { get; set; }
    }

    /// <summary>
    /// Rental counts by status, unique/repeat customers, average duration, and date range.
    /// </summary>
    public class RentalSummary
    {
        public int TotalRentals { get; set; }
        public int ActiveRentals { get; set; }
        public int ReturnedRentals { get; set; }
        public int OverdueRentals { get; set; }
        public int UniqueCustomers { get; set; }
        public int RepeatRenters { get; set; }
        public double AverageRentalDays { get; set; }
        public DateTime? FirstRentalDate { get; set; }
        public DateTime? LastRentalDate { get; set; }
    }

    /// <summary>
    /// Revenue split into base rental income and late fees, with per-rental average.
    /// </summary>
    public class RevenueBreakdown
    {
        public decimal TotalRevenue { get; set; }
        public decimal BaseRevenue { get; set; }
        public decimal LateFeeRevenue { get; set; }
        public decimal AverageRevenuePerRental { get; set; }
        public double LateFeePercentage { get; set; }
    }

    /// <summary>
    /// Customer membership tier distribution for a movie's renters.
    /// </summary>
    public class CustomerDemographicBreakdown
    {
        public Dictionary<string, int> TierDistribution { get; set; }
            = new Dictionary<string, int>();
        public int TotalUniqueCustomers { get; set; }
        public string DominantTier { get; set; }
    }

    /// <summary>
    /// A single data point in a monthly rental trend (count and revenue for one month).
    /// </summary>
    public class MonthlyRentalPoint
    {
        public string Month { get; set; }
        public int Year { get; set; }
        public int MonthNumber { get; set; }
        public int RentalCount { get; set; }
        public decimal Revenue { get; set; }
    }

    /// <summary>
    /// Weighted composite score (0–100) with sub-scores for popularity, revenue,
    /// retention, and rating, plus a letter grade.
    /// </summary>
    public class PerformanceScore
    {
        public double Popularity { get; set; }
        public double Revenue { get; set; }
        public double Retention { get; set; }
        public double Rating { get; set; }
        public double Overall { get; set; }
        public string Grade { get; set; }
    }

    /// <summary>
    /// Side-by-side comparison of two movies with per-metric winners and an overall verdict.
    /// </summary>
    public class MovieInsightComparison
    {
        public MovieInsight MovieA { get; set; }
        public MovieInsight MovieB { get; set; }
        public string RevenueWinner { get; set; }
        public string PopularityWinner { get; set; }
        public string PerformanceWinner { get; set; }
        public string OverallVerdict { get; set; }
    }
}
