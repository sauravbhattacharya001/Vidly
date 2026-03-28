using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    public enum TrendDirection
    {
        [Display(Name = "Rising")] Rising = 1,
        [Display(Name = "Stable")] Stable = 2,
        [Display(Name = "Cooling")] Cooling = 3,
        [Display(Name = "New Entry")] NewEntry = 4
    }

    public class MovieTrend
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public Genre? Genre { get; set; }
        public int RentalCount { get; set; }
        public int PreviousPeriodCount { get; set; }
        public TrendDirection Direction { get; set; }
        public double ChangePercent { get; set; }
        public int Rank { get; set; }
        public int? RankChange { get; set; }
        public double VelocityScore { get; set; }
    }

    public class GenreTrend
    {
        public Genre Genre { get; set; }
        public int RentalCount { get; set; }
        public int PreviousPeriodCount { get; set; }
        public TrendDirection Direction { get; set; }
        public double ChangePercent { get; set; }
        public double MarketShare { get; set; }
        public string TopMovie { get; set; }
    }

    public class DayOfWeekActivity
    {
        public DayOfWeek Day { get; set; }
        public int RentalCount { get; set; }
        public double Percentage { get; set; }
        public bool IsPeak { get; set; }
    }

    public class TrendsReport
    {
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public int PeriodDays { get; set; }
        public int TotalRentals { get; set; }
        public int PreviousPeriodRentals { get; set; }
        public double OverallChangePercent { get; set; }
        public List<MovieTrend> TopMovies { get; set; } = new List<MovieTrend>();
        public List<MovieTrend> BiggestMovers { get; set; } = new List<MovieTrend>();
        public List<MovieTrend> NewEntries { get; set; } = new List<MovieTrend>();
        public List<MovieTrend> FallingMovies { get; set; } = new List<MovieTrend>();
        public List<GenreTrend> GenreTrends { get; set; } = new List<GenreTrend>();
        public List<DayOfWeekActivity> DayOfWeekBreakdown { get; set; } = new List<DayOfWeekActivity>();
        public double AverageRentalsPerDay { get; set; }
        public DateTime? PeakDay { get; set; }
        public int PeakDayRentals { get; set; }
    }
}
