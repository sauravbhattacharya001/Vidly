using System;
using System.Collections.Generic;
using Vidly.Services;

namespace Vidly.Models
{
    // ── Enums ────────────────────────────────────────────────────────────

    /// <summary>Time granularity for trend aggregation.</summary>
    public enum TrendGranularity
    {
        Daily,
        Weekly,
        Monthly,
        Quarterly,
        Yearly
    }

    /// <summary>Severity of a trend signal.</summary>
    public enum TrendSignalStrength
    {
        Weak,
        Moderate,
        Strong
    }

    // ── Result models ────────────────────────────────────────────────────

    /// <summary>Rental count for a time bucket.</summary>
    public class RentalVolumeBucket
    {
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public int RentalCount { get; set; }
        public decimal Revenue { get; set; }
        public double AvgDailyRate { get; set; }
    }

    /// <summary>Volume time-series with overall direction.</summary>
    public class VolumeTimeSeries
    {
        public TrendGranularity Granularity { get; set; }
        public List<RentalVolumeBucket> Buckets { get; set; } = new List<RentalVolumeBucket>();
        public TrendDirection OverallDirection { get; set; }
        public double ChangePercent { get; set; }
    }

    /// <summary>Genre popularity snapshot for a period.</summary>
    public class GenreMomentum
    {
        public string Genre { get; set; }
        public int CurrentPeriodRentals { get; set; }
        public int PreviousPeriodRentals { get; set; }
        public double ChangePercent { get; set; }
        public TrendDirection Direction { get; set; }
        public int Rank { get; set; }
        public int PreviousRank { get; set; }
        public int RankChange { get; set; }
    }

    /// <summary>Day-of-week / hour-of-day rental heatmap cell.</summary>
    public class PeakPeriodCell
    {
        public DayOfWeek DayOfWeek { get; set; }
        public int Hour { get; set; }
        public int RentalCount { get; set; }
        public double Intensity { get; set; } // 0.0 – 1.0
    }

    /// <summary>Peak period analysis result.</summary>
    public class PeakPeriodAnalysis
    {
        public List<PeakPeriodCell> Cells { get; set; } = new List<PeakPeriodCell>();
        public DayOfWeek PeakDay { get; set; }
        public int PeakHour { get; set; }
        public DayOfWeek QuietestDay { get; set; }
        public int QuietestHour { get; set; }
        public double WeekdayAvg { get; set; }
        public double WeekendAvg { get; set; }
    }

    /// <summary>A movie that is trending (velocity increase).</summary>
    public class TrendingMovie
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public string Genre { get; set; }
        public int RecentRentals { get; set; }
        public int PriorRentals { get; set; }
        public double VelocityChange { get; set; }
        public TrendSignalStrength Signal { get; set; }
    }

    /// <summary>Customer rental behaviour cluster.</summary>
    public class CustomerSegmentTrend
    {
        public string Segment { get; set; } // e.g. "Heavy", "Moderate", "Light", "Lapsed"
        public int CustomerCount { get; set; }
        public int TotalRentals { get; set; }
        public double AvgRentalsPerCustomer { get; set; }
        public decimal AvgRevenue { get; set; }
    }

    /// <summary>Full trend report combining all analyses.</summary>
    public class TrendReport
    {
        public DateTime GeneratedAt { get; set; }
        public DateTime AnalysisStart { get; set; }
        public DateTime AnalysisEnd { get; set; }
        public VolumeTimeSeries Volume { get; set; }
        public List<GenreMomentum> GenreMomentum { get; set; } = new List<GenreMomentum>();
        public PeakPeriodAnalysis PeakPeriods { get; set; }
        public List<TrendingMovie> TrendingMovies { get; set; } = new List<TrendingMovie>();
        public List<TrendingMovie> DecliningMovies { get; set; } = new List<TrendingMovie>();
        public List<CustomerSegmentTrend> CustomerSegments { get; set; } = new List<CustomerSegmentTrend>();
        public List<string> Insights { get; set; } = new List<string>();
    }
}
