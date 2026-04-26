using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    public class DemandForecast
    {
        public DateTime GeneratedAt { get; set; }
        public List<MovieForecast> MovieForecasts { get; set; } = new List<MovieForecast>();
        public List<GenreHeatmapEntry> GenreHeatmap { get; set; } = new List<GenreHeatmapEntry>();
        public List<StockAlert> StockAlerts { get; set; } = new List<StockAlert>();
        public List<RestockRecommendation> Recommendations { get; set; } = new List<RestockRecommendation>();
        public DemandSummary Summary { get; set; } = new DemandSummary();
    }

    public class MovieForecast
    {
        public int MovieId { get; set; }
        public string Name { get; set; }
        public string Genre { get; set; }
        public double[] DailyPredictions { get; set; } = new double[7];
        public string TrendDirection { get; set; }
        public bool SurgeDetected { get; set; }
        public double HistoricalAverage { get; set; }
    }

    public class GenreHeatmapEntry
    {
        public string Genre { get; set; }
        public string DayOfWeek { get; set; }
        public double PredictedDemand { get; set; }
        public double HistoricalAvg { get; set; }
    }

    public class StockAlert
    {
        public string MovieName { get; set; }
        public int CurrentStock { get; set; }
        public double PredictedDemand { get; set; }
        public string RiskLevel { get; set; }
        public int DaysUntilStockout { get; set; }
    }

    public class RestockRecommendation
    {
        public string MovieName { get; set; }
        public string Genre { get; set; }
        public int RecommendedQuantity { get; set; }
        public string Urgency { get; set; }
        public string Reason { get; set; }
    }

    public class DemandSummary
    {
        public double TotalPredictedRentals { get; set; }
        public string TopGenre { get; set; }
        public string TopMovie { get; set; }
        public int AlertCount { get; set; }
        public int HealthScore { get; set; }
    }
}
