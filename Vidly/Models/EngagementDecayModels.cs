using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>Engagement decay phase classification.</summary>
    public enum EngagementPhase
    {
        Active = 1,
        Cooling = 2,
        Dormant = 3,
        AtRisk = 4,
        Churned = 5
    }

    /// <summary>Type of re-engagement intervention.</summary>
    public enum InterventionType
    {
        GenreReminder = 1,
        NewReleaseAlert = 2,
        LoyaltyBonus = 3,
        PersonalizedPick = 4,
        WinBackOffer = 5,
        MilestoneReminder = 6
    }

    /// <summary>Per-customer engagement profile with decay metrics.</summary>
    public class CustomerEngagementProfile
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public EngagementPhase CurrentPhase { get; set; }
        public double EngagementScore { get; set; }
        public double DecayRate { get; set; }
        public int DaysSinceLastRental { get; set; }
        public int TotalRentals { get; set; }
        public int RentalsLast30Days { get; set; }
        public int RentalsLast90Days { get; set; }
        public double AverageInterRentalDays { get; set; }
        public double PredictedDaysToChurn { get; set; }
        public string PreferredGenre { get; set; }
        public DateTime? LastRentalDate { get; set; }
        public DateTime? PredictedNextRentalDate { get; set; }
        public string PhaseTransitionWarning { get; set; }
    }

    /// <summary>Re-engagement window prediction.</summary>
    public class ReengagementWindow
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public DateTime WindowStart { get; set; }
        public DateTime WindowEnd { get; set; }
        public double Confidence { get; set; }
        public string Reason { get; set; }
        public InterventionType RecommendedIntervention { get; set; }
    }

    /// <summary>Proactive intervention recommendation.</summary>
    public class EngagementIntervention
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public InterventionType Type { get; set; }
        public string Message { get; set; }
        public double Priority { get; set; }
        public double ExpectedImpact { get; set; }
        public string Rationale { get; set; }
    }

    /// <summary>Fleet-wide engagement health metrics.</summary>
    public class EngagementFleetHealth
    {
        public double OverallHealthScore { get; set; }
        public int TotalCustomers { get; set; }
        public int ActiveCount { get; set; }
        public int CoolingCount { get; set; }
        public int DormantCount { get; set; }
        public int AtRiskCount { get; set; }
        public int ChurnedCount { get; set; }
        public double ActivePercentage { get; set; }
        public double ChurnRate { get; set; }
        public double AverageEngagementScore { get; set; }
        public string HealthTier { get; set; }
        public string Trend { get; set; }
    }

    /// <summary>Engagement decay trend data point.</summary>
    public class EngagementTrendPoint
    {
        public DateTime Date { get; set; }
        public double AverageScore { get; set; }
        public int ActiveCount { get; set; }
        public int ChurnedCount { get; set; }
    }

    /// <summary>Full engagement decay report.</summary>
    public class EngagementDecayReport
    {
        public DateTime GeneratedAt { get; set; }
        public EngagementFleetHealth FleetHealth { get; set; }
        public List<CustomerEngagementProfile> Profiles { get; set; }
        public List<ReengagementWindow> Windows { get; set; }
        public List<EngagementIntervention> Interventions { get; set; }
        public List<EngagementTrendPoint> TrendHistory { get; set; }
        public List<string> Insights { get; set; }
        public double EngagementDecayScore { get; set; }
    }
}
