using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    // ─── Configuration ───────────────────────────────────────────────────────
    public class ProcurementConfig
    {
        public int AnalysisWindowDays { get; set; } = 90;
        public int ForecastHorizonDays { get; set; } = 60;
        public decimal DefaultDailyRate { get; set; } = 3.50m;
        public decimal AcquisitionCostPerTitle { get; set; } = 25.00m;
        public int MinRentalsForSignal { get; set; } = 3;
        public double DemandGrowthThreshold { get; set; } = 0.15;
        public double SupplyAdequacyTarget { get; set; } = 0.70;
        public int MaxRecommendations { get; set; } = 10;
    }

    // ─── Enums ───────────────────────────────────────────────────────────────
    public enum DemandSignalType
    {
        HighVelocity,
        GrowingTrend,
        GenreGap,
        SeasonalSurge,
        UnderservedSegment,
        CompetitivePressure
    }

    public enum ProcurementUrgency
    {
        Critical,
        High,
        Medium,
        Low,
        Monitor
    }

    public enum BudgetAllocationStrategy
    {
        DemandDriven,
        DiversityFocused,
        RoiMaximized,
        Balanced
    }

    // ─── Domain Models ───────────────────────────────────────────────────────
    public class DemandSignal
    {
        public DemandSignalType Type { get; set; }
        public Genre? Genre { get; set; }
        public double Strength { get; set; }
        public string Description { get; set; }
        public DateTime DetectedAt { get; set; }
        public Dictionary<string, double> Evidence { get; set; } = new Dictionary<string, double>();
    }

    public class GenreSupplyProfile
    {
        public Genre Genre { get; set; }
        public int TitleCount { get; set; }
        public int RecentRentals { get; set; }
        public double RentalsPerTitle { get; set; }
        public double SupplyAdequacy { get; set; }
        public double DemandGrowthRate { get; set; }
        public double ShareOfCatalog { get; set; }
        public double ShareOfDemand { get; set; }
        public double SupplyDemandRatio { get; set; }
        public bool IsUnderserved { get; set; }
    }

    public class AcquisitionCandidate
    {
        public Genre Genre { get; set; }
        public int RecommendedCopies { get; set; }
        public ProcurementUrgency Urgency { get; set; }
        public double ConfidenceScore { get; set; }
        public decimal EstimatedAcquisitionCost { get; set; }
        public decimal ProjectedMonthlyRevenue { get; set; }
        public decimal ProjectedRoi { get; set; }
        public int PaybackDays { get; set; }
        public List<string> Rationale { get; set; } = new List<string>();
        public List<DemandSignal> SupportingSignals { get; set; } = new List<DemandSignal>();
    }

    public class BudgetAllocation
    {
        public Genre Genre { get; set; }
        public decimal AllocatedBudget { get; set; }
        public double AllocationPercent { get; set; }
        public int TitlesToAcquire { get; set; }
        public string Justification { get; set; }
    }

    public class ProcurementInsight
    {
        public string Category { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public double Impact { get; set; }
    }

    // ─── Report ──────────────────────────────────────────────────────────────
    public class ProcurementReport
    {
        public DateTime GeneratedAt { get; set; }
        public int HealthScore { get; set; }
        public string HealthVerdict { get; set; }

        // Supply analysis
        public List<GenreSupplyProfile> SupplyProfiles { get; set; } = new List<GenreSupplyProfile>();
        public int TotalCatalogSize { get; set; }
        public int UnderservedGenres { get; set; }

        // Demand signals
        public List<DemandSignal> Signals { get; set; } = new List<DemandSignal>();

        // Recommendations
        public List<AcquisitionCandidate> Candidates { get; set; } = new List<AcquisitionCandidate>();

        // Budget
        public decimal TotalBudgetRecommended { get; set; }
        public List<BudgetAllocation> BudgetPlan { get; set; } = new List<BudgetAllocation>();
        public BudgetAllocationStrategy Strategy { get; set; }

        // Insights
        public List<ProcurementInsight> Insights { get; set; } = new List<ProcurementInsight>();

        // Summary stats
        public decimal TotalProjectedRoi { get; set; }
        public int TotalTitlesToAcquire { get; set; }
        public int AveragePaybackDays { get; set; }
    }
}
