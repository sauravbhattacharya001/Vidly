using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>Why a customer lapsed.</summary>
    public enum LapseReason
    {
        Inactivity,
        PriceShock,
        NarrowTaste,
        BadExperience,
        SeasonalDropoff,
        Unknown
    }

    /// <summary>Campaign type matched to lapse reason.</summary>
    public enum CampaignType
    {
        WelcomeBack,
        PriceIncentive,
        GenreExpansion,
        LoyaltyRecovery,
        SeasonalReminder,
        PersonalPick
    }

    /// <summary>Win-back case lifecycle status.</summary>
    public enum WinBackStatus
    {
        Identified,
        Campaigned,
        Recovered,
        Lost
    }

    /// <summary>A single customer win-back case with diagnosis and campaign.</summary>
    public class WinBackCase
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string Email { get; set; }
        public MembershipType MembershipType { get; set; }
        public LapseReason LapseReason { get; set; }
        public CampaignType RecommendedCampaign { get; set; }
        public WinBackStatus Status { get; set; }
        public DateTime IdentifiedDate { get; set; }
        public DateTime? LastRentalDate { get; set; }
        public int DaysSinceLastRental { get; set; }
        public int TotalLifetimeRentals { get; set; }
        public decimal TotalLifetimeSpend { get; set; }
        public decimal LateFeeRatio { get; set; }
        public string TopGenre { get; set; }
        public double GenreDiversity { get; set; }
        public List<string> CampaignMessages { get; set; } = new List<string>();
        public double WinBackProbability { get; set; }
        public string PersonalizedOffer { get; set; }
    }

    /// <summary>Win-back engine dashboard summary.</summary>
    public class WinBackSummary
    {
        public DateTime AnalysisDate { get; set; }
        public int TotalLapsedCustomers { get; set; }
        public int HighProbabilityTargets { get; set; }
        public int CampaignsGenerated { get; set; }
        public int RecoveredCustomers { get; set; }
        public decimal EstimatedRecoverableRevenue { get; set; }
        public Dictionary<LapseReason, int> ReasonBreakdown { get; set; } = new Dictionary<LapseReason, int>();
        public Dictionary<CampaignType, int> CampaignBreakdown { get; set; } = new Dictionary<CampaignType, int>();
        public List<WinBackCase> Cases { get; set; } = new List<WinBackCase>();
        public List<string> ProactiveInsights { get; set; } = new List<string>();
    }
}
