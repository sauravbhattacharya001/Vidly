using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    public enum MarketPosition
    {
        Leader,
        Competitive,
        AtParity,
        Trailing,
        Vulnerable
    }

    public enum OpportunityType
    {
        PriceGap,
        DemandSurge,
        CompetitorWeakness,
        SeasonalWindow,
        NicheMonopoly,
        GenreGap
    }

    public enum ThreatLevel
    {
        Low,
        Moderate,
        High,
        Critical
    }

    public enum StrategicMove
    {
        AggressiveDiscount,
        PremiumPositioning,
        BundleDefense,
        NicheCapture,
        PriceMatch,
        FlashSale,
        LossLeader
    }

    public class CompetitorBenchmark
    {
        public string CompetitorName { get; set; }
        public Genre Genre { get; set; }
        public decimal AvgDailyRate { get; set; }
        public int CatalogSize { get; set; }
        public double CustomerSatisfaction { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class MarketPositionAssessment
    {
        public Genre Genre { get; set; }
        public MarketPosition Position { get; set; }
        public decimal OurAvgPrice { get; set; }
        public decimal MarketAvgPrice { get; set; }
        public decimal PriceGapPercent { get; set; }
        public int OurCatalogCount { get; set; }
        public int AvgCompetitorCatalogCount { get; set; }
        public string Assessment { get; set; }
    }

    public class MarketOpportunity
    {
        public OpportunityType Type { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public Genre? Genre { get; set; }
        public decimal EstimatedRevenueImpact { get; set; }
        public int ConfidencePercent { get; set; }
        public DateTime DetectedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public StrategicMove RecommendedMove { get; set; }
    }

    public class CompetitiveThreat
    {
        public ThreatLevel Level { get; set; }
        public string Source { get; set; }
        public string Description { get; set; }
        public Genre? AffectedGenre { get; set; }
        public decimal PotentialRevenueLoss { get; set; }
        public List<StrategicMove> CounterMoves { get; set; }
        public string Urgency { get; set; }
    }

    public class StrategicRecommendation
    {
        public StrategicMove Move { get; set; }
        public string Title { get; set; }
        public string Rationale { get; set; }
        public Genre? TargetGenre { get; set; }
        public decimal ExpectedRevenueChange { get; set; }
        public int ConfidencePercent { get; set; }
        public string Implementation { get; set; }
        public ThreatLevel RiskLevel { get; set; }
    }

    public class CompetitiveIntelDashboard
    {
        public List<MarketPositionAssessment> PositionMap { get; set; }
        public List<MarketOpportunity> Opportunities { get; set; }
        public List<CompetitiveThreat> Threats { get; set; }
        public List<StrategicRecommendation> Recommendations { get; set; }
        public List<CompetitorBenchmark> Benchmarks { get; set; }
        public CompetitiveHealthScore HealthScore { get; set; }
        public List<string> AutonomousInsights { get; set; }
    }

    public class CompetitiveHealthScore
    {
        public int Overall { get; set; }
        public int PricingStrength { get; set; }
        public int CatalogCoverage { get; set; }
        public int OpportunityCapture { get; set; }
        public int ThreatResilience { get; set; }
        public string Grade { get; set; }
    }
}
