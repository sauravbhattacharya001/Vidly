using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    public enum Recommendation
    {
        Hold,
        AcquireMore,
        Retire,
        MarkDown,
        Promote
    }

    public enum RecommendationReason
    {
        HighDemand,
        LowUtilization,
        RevenueDeclining,
        SeasonalTrend,
        GenreGap,
        AgingStock
    }

    public enum ActionPriority
    {
        Critical,
        High,
        Medium,
        Low
    }

    public class TitleAnalysis
    {
        public int MovieId { get; set; }
        public string Title { get; set; }
        public string Genre { get; set; }
        public int CopiesOwned { get; set; }
        public double AvgRentalsPerWeek { get; set; }
        public int DemandScore { get; set; }
        public double UtilizationRate { get; set; }
        public decimal RevenuePerCopy { get; set; }
        public Recommendation Recommendation { get; set; }
        public RecommendationReason Reason { get; set; }
    }

    public class DemandForecast
    {
        public int WeekNumber { get; set; }
        public string WeekLabel { get; set; }
        public double PredictedRentals { get; set; }
        public double ConfidenceLow { get; set; }
        public double ConfidenceHigh { get; set; }
    }

    public class OptimizationAction
    {
        public ActionPriority Priority { get; set; }
        public string Description { get; set; }
        public string MovieTitle { get; set; }
        public decimal EstimatedRevenueImpact { get; set; }
    }

    public class InventoryHealthScore
    {
        public int OverallScore { get; set; }
        public double UtilizationEfficiency { get; set; }
        public double DemandCoverage { get; set; }
        public double RevenueOptimality { get; set; }
        public double StaleInventoryRatio { get; set; }
    }

    public class InventoryOptimizerResult
    {
        public InventoryHealthScore HealthScore { get; set; }
        public List<TitleAnalysis> TitleAnalyses { get; set; }
        public List<DemandForecast> Forecasts { get; set; }
        public List<OptimizationAction> Actions { get; set; }
        public DateTime GeneratedAt { get; set; }
        public bool AutoOptimizeEnabled { get; set; }
    }
}
