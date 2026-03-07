using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// Represents a customer referral — one customer refers another to join Vidly.
    /// </summary>
    public class Referral
    {
        public int Id { get; set; }

        /// <summary>The customer who made the referral.</summary>
        public int ReferrerId { get; set; }

        /// <summary>Name of the referred person (before they become a customer).</summary>
        [Required]
        [StringLength(255)]
        public string ReferredName { get; set; }

        /// <summary>Email of the referred person.</summary>
        [Required]
        [EmailAddress]
        [StringLength(255)]
        public string ReferredEmail { get; set; }

        /// <summary>If the referred person signed up, their customer ID.</summary>
        public int? ReferredCustomerId { get; set; }

        /// <summary>Unique referral code for tracking.</summary>
        [Required]
        [StringLength(20)]
        public string ReferralCode { get; set; }

        public DateTime CreatedDate { get; set; }
        public DateTime? ConvertedDate { get; set; }
        public DateTime? RewardClaimedDate { get; set; }

        public ReferralStatus Status { get; set; }

        /// <summary>Bonus points awarded to referrer on conversion.</summary>
        public int PointsAwarded { get; set; }
    }

    public enum ReferralStatus
    {
        [Display(Name = "Pending")]
        Pending = 0,

        [Display(Name = "Converted")]
        Converted = 1,

        [Display(Name = "Reward Claimed")]
        RewardClaimed = 2,

        [Display(Name = "Expired")]
        Expired = 3
    }

    /// <summary>
    /// Summary stats for a customer's referral activity.
    /// </summary>
    public class ReferralSummary
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int TotalReferrals { get; set; }
        public int ConvertedCount { get; set; }
        public int PendingCount { get; set; }
        public int ExpiredCount { get; set; }
        public int TotalPointsEarned { get; set; }
        public double ConversionRate { get; set; }
        public string ReferralCode { get; set; }
    }

    /// <summary>
    /// Leaderboard entry for top referrers.
    /// </summary>
    public class ReferralLeaderboardEntry
    {
        public int Rank { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int ConvertedReferrals { get; set; }
        public int TotalPointsEarned { get; set; }
        public string Tier { get; set; }
    }

    /// <summary>
    /// Program-wide referral analytics.
    /// </summary>
    public class ReferralProgramStats
    {
        public int TotalReferrals { get; set; }
        public int TotalConverted { get; set; }
        public int TotalPending { get; set; }
        public int TotalExpired { get; set; }
        public double OverallConversionRate { get; set; }
        public int TotalPointsAwarded { get; set; }
        public int ActiveReferrers { get; set; }
        public IReadOnlyList<ReferralLeaderboardEntry> Leaderboard { get; set; }
        public IReadOnlyList<MonthlyReferralTrend> MonthlyTrends { get; set; }
    }

    /// <summary>
    /// Monthly trend data for referral charts.
    /// </summary>
    public class MonthlyReferralTrend
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string Label { get; set; }
        public int Sent { get; set; }
        public int Converted { get; set; }
    }
}
