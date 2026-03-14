using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// Day-of-week rental volume breakdown.
    /// </summary>
    public class DayOfWeekBreakdown
    {
        public DayOfWeek Day { get; set; }
        public int RentalCount { get; set; }
        public decimal Percentage { get; set; }
        public decimal AverageRevenue { get; set; }
    }

    /// <summary>
    /// Genre popularity within a time period.
    /// </summary>
    public class GenreTrend
    {
        public Genre Genre { get; set; }
        public int RentalCount { get; set; }
        public decimal Percentage { get; set; }
        public decimal TotalRevenue { get; set; }
        public int UniqueCustomers { get; set; }
        public int Direction { get; set; } // +1 rising, 0 stable, -1 falling
    }

    /// <summary>
    /// Monthly rental volume data point.
    /// </summary>
    public class MonthlyVolume
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public int RentalCount { get; set; }
        public decimal Revenue { get; set; }
        public int UniqueCustomers { get; set; }
        public int ChangePercent { get; set; } // vs previous month
    }

    /// <summary>
    /// Customer retention cohort (customers grouped by first-rental month).
    /// </summary>
    public class RetentionCohort
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public int CohortSize { get; set; }
        public int ReturnedWithin30Days { get; set; }
        public int ReturnedWithin90Days { get; set; }
        public decimal RetentionRate30 { get; set; }
        public decimal RetentionRate90 { get; set; }
    }

    /// <summary>
    /// Peak and quiet period identification.
    /// </summary>
    public class PeakPeriod
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int RentalCount { get; set; }
        public decimal DailyAverage { get; set; }
        public bool IsPeak { get; set; } // true = peak, false = quiet
    }

    /// <summary>
    /// Comprehensive trend analysis report.
    /// </summary>
    public class RentalTrendReport
    {
        public DateTime AnalysisStart { get; set; }
        public DateTime AnalysisEnd { get; set; }
        public int TotalRentals { get; set; }
        public decimal TotalRevenue { get; set; }
        public List<DayOfWeekBreakdown> DayOfWeekBreakdown { get; set; }
            = new List<DayOfWeekBreakdown>();
        public List<GenreTrend> GenreTrends { get; set; }
            = new List<GenreTrend>();
        public List<MonthlyVolume> MonthlyVolumes { get; set; }
            = new List<MonthlyVolume>();
        public List<RetentionCohort> RetentionCohorts { get; set; }
            = new List<RetentionCohort>();
        public List<PeakPeriod> PeakPeriods { get; set; }
            = new List<PeakPeriod>();
        public DayOfWeek BusiestDay { get; set; }
        public DayOfWeek QuietestDay { get; set; }
        public Genre? TopGenre { get; set; }
        public decimal AverageRentalsPerDay { get; set; }
    }
}
