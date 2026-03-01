using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// Enriched rental record with computed fields for history display.
    /// </summary>
    public class RentalHistoryEntry
    {
        public int RentalId { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public Genre? MovieGenre { get; set; }
        public DateTime RentalDate { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime? ReturnDate { get; set; }
        public RentalStatus Status { get; set; }
        public decimal DailyRate { get; set; }
        public decimal LateFee { get; set; }
        public decimal TotalCost { get; set; }
        public int RentalDurationDays { get; set; }
        public int DaysOverdue { get; set; }
        public bool WasLate { get; set; }
    }

    /// <summary>
    /// Timeline event types for customer activity.
    /// </summary>
    public enum TimelineEventType
    {
        Rented = 1,
        Returned = 2,
        LateFee = 3,
        WatchlistAdd = 4,
        OverdueWarning = 5
    }

    /// <summary>
    /// A single event in a customer's activity timeline.
    /// </summary>
    public class TimelineEvent
    {
        public DateTime Date { get; set; }
        public TimelineEventType EventType { get; set; }
        public string Description { get; set; }
        public int? RentalId { get; set; }
        public int? MovieId { get; set; }
        public string MovieName { get; set; }
        public decimal? Amount { get; set; }
    }

    /// <summary>
    /// Analysis of busiest rental hours and days of week.
    /// </summary>
    public class PopularTimesResult
    {
        public Dictionary<DayOfWeek, int> RentalsByDayOfWeek { get; set; } = new Dictionary<DayOfWeek, int>();
        public Dictionary<int, int> RentalsByHour { get; set; } = new Dictionary<int, int>();
        public DayOfWeek? BusiestDay { get; set; }
        public int? BusiestHour { get; set; }
        public int TotalRentals { get; set; }
    }

    /// <summary>
    /// Customer retention and churn metrics.
    /// </summary>
    public class RetentionMetrics
    {
        public double ReturnRate { get; set; }
        public double RepeatRentalRate { get; set; }
        public double AverageGapDays { get; set; }
        public List<CustomerChurnRisk> ChurnRisks { get; set; } = new List<CustomerChurnRisk>();
        public int TotalCustomers { get; set; }
        public int RepeatCustomers { get; set; }
    }

    /// <summary>
    /// Churn risk assessment for a single customer.
    /// </summary>
    public class CustomerChurnRisk
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int TotalRentals { get; set; }
        public double DaysSinceLastRental { get; set; }
        public string RiskLevel { get; set; } // Low, Medium, High
    }

    /// <summary>
    /// Per-movie inventory availability prediction.
    /// </summary>
    public class InventoryForecast
    {
        public int DaysAhead { get; set; }
        public List<MovieAvailabilityPrediction> Predictions { get; set; } = new List<MovieAvailabilityPrediction>();
    }

    /// <summary>
    /// Predicted availability for a single movie.
    /// </summary>
    public class MovieAvailabilityPrediction
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public bool CurrentlyAvailable { get; set; }
        public DateTime? EstimatedAvailableDate { get; set; }
        public double AverageRentalDurationDays { get; set; }
    }

    /// <summary>
    /// Loyalty score result for a customer.
    /// </summary>
    public class LoyaltyResult
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int Score { get; set; } // 0-100
        public string Tier { get; set; } // Bronze, Silver, Gold, Platinum
        public LoyaltyBreakdown Breakdown { get; set; }
    }

    /// <summary>
    /// Breakdown of loyalty score components.
    /// </summary>
    public class LoyaltyBreakdown
    {
        public int FrequencyPoints { get; set; }
        public int RecencyPoints { get; set; }
        public int OnTimePoints { get; set; }
        public int SpendPoints { get; set; }
    }

    /// <summary>
    /// Genre popularity trend for a specific month.
    /// </summary>
    public class SeasonalTrend
    {
        public Genre Genre { get; set; }
        public int Month { get; set; }
        public int Count { get; set; }
    }

    /// <summary>
    /// Report types for GenerateReport.
    /// </summary>
    public enum ReportType
    {
        Summary = 1,
        Detailed = 2,
        CustomerFocused = 3,
        MovieFocused = 4
    }

    /// <summary>
    /// A formatted report with title and sections.
    /// </summary>
    public class RentalReport
    {
        public ReportType Type { get; set; }
        public string Title { get; set; }
        public DateTime GeneratedAt { get; set; }
        public List<ReportSection> Sections { get; set; } = new List<ReportSection>();
    }

    /// <summary>
    /// A section within a report.
    /// </summary>
    public class ReportSection
    {
        public string Heading { get; set; }
        public string Content { get; set; }
    }
}
