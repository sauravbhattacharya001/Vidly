using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// Rental insurance policy that covers late fees, damage charges,
    /// and lost-disc replacement for a specific rental.
    /// </summary>
    public class InsurancePolicy
    {
        public int Id { get; set; }
        public int RentalId { get; set; }
        public int CustomerId { get; set; }
        public InsuranceTier Tier { get; set; }
        public decimal Premium { get; set; }
        public DateTime PurchasedAt { get; set; }
        public InsurancePolicyStatus Status { get; set; }

        /// <summary>Maximum payout for this policy.</summary>
        public decimal CoverageLimit { get; set; }

        /// <summary>Total claimed so far.</summary>
        public decimal TotalClaimed { get; set; }
    }

    /// <summary>
    /// A claim filed against an insurance policy.
    /// </summary>
    public class InsuranceClaim
    {
        public int Id { get; set; }
        public int PolicyId { get; set; }
        public int RentalId { get; set; }
        public int CustomerId { get; set; }
        public ClaimType ClaimType { get; set; }
        public decimal Amount { get; set; }
        public DateTime FiledAt { get; set; }
        public ClaimStatus Status { get; set; }
        public string DenialReason { get; set; }
    }

    public enum InsuranceTier
    {
        [Display(Name = "Basic")]
        Basic = 1,

        [Display(Name = "Standard")]
        Standard = 2,

        [Display(Name = "Premium")]
        Premium = 3
    }

    public enum InsurancePolicyStatus
    {
        Active = 1,
        Claimed = 2,
        Expired = 3,
        Cancelled = 4
    }

    public enum ClaimType
    {
        [Display(Name = "Late Fee")]
        LateFee = 1,

        [Display(Name = "Damage")]
        Damage = 2,

        [Display(Name = "Lost Disc")]
        LostDisc = 3
    }

    public enum ClaimStatus
    {
        Pending = 1,
        Approved = 2,
        Denied = 3,
        Paid = 4
    }

    /// <summary>Insurance analytics summary.</summary>
    public class InsuranceAnalytics
    {
        public int TotalPolicies { get; set; }
        public int ActivePolicies { get; set; }
        public decimal TotalPremiums { get; set; }
        public decimal TotalPayouts { get; set; }
        public decimal ProfitMargin { get; set; }
        public int TotalClaims { get; set; }
        public int ApprovedClaims { get; set; }
        public int DeniedClaims { get; set; }
        public decimal ApprovalRate { get; set; }
        public decimal AverageClaimAmount { get; set; }
        public Dictionary<InsuranceTier, int> PoliciesByTier { get; set; }
        public Dictionary<ClaimType, int> ClaimsByType { get; set; }
        public decimal LossRatio { get; set; }
    }
}
