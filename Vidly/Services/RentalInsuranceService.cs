using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Manages optional rental insurance — customers can purchase
    /// a policy at checkout that covers late fees, damage charges,
    /// and lost-disc replacement. Three tiers (Basic/Standard/Premium)
    /// with different coverage limits and premiums.
    /// </summary>
    public class RentalInsuranceService
    {
        private readonly IRentalRepository _rentalRepo;
        private readonly ICustomerRepository _customerRepo;
        private readonly IClock _clock;

        // In-memory stores (production would use a DB)
        private readonly List<InsurancePolicy> _policies = new List<InsurancePolicy>();
        private readonly List<InsuranceClaim> _claims = new List<InsuranceClaim>();
        private int _nextPolicyId = 1;
        private int _nextClaimId = 1;

        // ── Tier Configuration ───────────────────────────────────────

        /// <summary>Premium multiplier applied to the rental daily rate.</summary>
        public static readonly Dictionary<InsuranceTier, decimal> PremiumMultiplier =
            new Dictionary<InsuranceTier, decimal>
            {
                { InsuranceTier.Basic, 0.15m },      // 15% of daily rate
                { InsuranceTier.Standard, 0.30m },   // 30% of daily rate
                { InsuranceTier.Premium, 0.50m }     // 50% of daily rate
            };

        /// <summary>Maximum coverage per policy tier.</summary>
        public static readonly Dictionary<InsuranceTier, decimal> CoverageLimits =
            new Dictionary<InsuranceTier, decimal>
            {
                { InsuranceTier.Basic, 10.00m },     // Covers up to $10
                { InsuranceTier.Standard, 25.00m },  // Covers up to $25
                { InsuranceTier.Premium, 50.00m }    // Covers up to $50
            };

        /// <summary>Which claim types each tier covers.</summary>
        public static readonly Dictionary<InsuranceTier, HashSet<ClaimType>> TierCoverage =
            new Dictionary<InsuranceTier, HashSet<ClaimType>>
            {
                { InsuranceTier.Basic, new HashSet<ClaimType> { ClaimType.LateFee } },
                { InsuranceTier.Standard, new HashSet<ClaimType> { ClaimType.LateFee, ClaimType.Damage } },
                { InsuranceTier.Premium, new HashSet<ClaimType> { ClaimType.LateFee, ClaimType.Damage, ClaimType.LostDisc } }
            };

        public RentalInsuranceService(
            IRentalRepository rentalRepo,
            ICustomerRepository customerRepo,
            IClock clock = null)
        {
            _rentalRepo = rentalRepo ?? throw new ArgumentNullException(nameof(rentalRepo));
            _customerRepo = customerRepo ?? throw new ArgumentNullException(nameof(customerRepo));
        }

        // ── Purchase ─────────────────────────────────────────────────

        /// <summary>
        /// Purchase an insurance policy for a rental.
        /// </summary>
        public InsurancePolicy Purchase(int rentalId, int customerId, InsuranceTier tier)
        {
            var rental = _rentalRepo.GetById(rentalId);
            if (rental == null)
                throw new InvalidOperationException("Rental not found.");
            if (rental.CustomerId != customerId)
                throw new InvalidOperationException("Rental does not belong to this customer.");
            if (_customerRepo.GetById(customerId) == null)
                throw new InvalidOperationException("Customer not found.");
            if (rental.Status == RentalStatus.Returned)
                throw new InvalidOperationException("Cannot insure a returned rental.");

            // Check for existing active policy on this rental
            if (_policies.Any(p => p.RentalId == rentalId && p.Status == InsurancePolicyStatus.Active))
                throw new InvalidOperationException("Rental already has an active insurance policy.");

            var premium = CalculatePremium(rental.DailyRate, tier);
            var coverageLimit = CoverageLimits[tier];

            var policy = new InsurancePolicy
            {
                Id = _nextPolicyId++,
                RentalId = rentalId,
                CustomerId = customerId,
                Tier = tier,
                Premium = premium,
                PurchasedAt = _clock.Now,
                Status = InsurancePolicyStatus.Active,
                CoverageLimit = coverageLimit,
                TotalClaimed = 0
            };

            _policies.Add(policy);
            return policy;
        }

        /// <summary>
        /// Calculate the premium for a given daily rate and tier.
        /// </summary>
        public decimal CalculatePremium(decimal dailyRate, InsuranceTier tier)
        {
            if (dailyRate <= 0)
                throw new ArgumentException("Daily rate must be positive.", nameof(dailyRate));

            return Math.Round(dailyRate * PremiumMultiplier[tier], 2);
        }

        /// <summary>
        /// Get a price quote for all tiers for a rental.
        /// </summary>
        public Dictionary<InsuranceTier, decimal> GetQuotes(int rentalId)
        {
            var rental = _rentalRepo.GetById(rentalId);
            if (rental == null)
                throw new InvalidOperationException("Rental not found.");

            var quotes = new Dictionary<InsuranceTier, decimal>();
            foreach (InsuranceTier tier in Enum.GetValues(typeof(InsuranceTier)))
            {
                quotes[tier] = CalculatePremium(rental.DailyRate, tier);
            }
            return quotes;
        }

        // ── Claims ───────────────────────────────────────────────────

        /// <summary>
        /// File a claim against an insurance policy.
        /// </summary>
        public InsuranceClaim FileClaim(int policyId, ClaimType claimType, decimal amount)
        {
            if (amount <= 0)
                throw new ArgumentException("Claim amount must be positive.", nameof(amount));

            var policy = _policies.FirstOrDefault(p => p.Id == policyId);
            if (policy == null)
                throw new InvalidOperationException("Policy not found.");
            if (policy.Status != InsurancePolicyStatus.Active)
                throw new InvalidOperationException("Policy is not active.");

            // Check tier coverage
            if (!TierCoverage[policy.Tier].Contains(claimType))
                throw new InvalidOperationException(
                    $"{policy.Tier} tier does not cover {claimType} claims. " +
                    $"Covered types: {string.Join(", ", TierCoverage[policy.Tier])}.");

            // Check remaining coverage
            var remaining = policy.CoverageLimit - policy.TotalClaimed;
            if (remaining <= 0)
                throw new InvalidOperationException("Policy coverage limit has been exhausted.");

            // Cap the approved amount at remaining coverage
            var approvedAmount = Math.Min(amount, remaining);

            var claim = new InsuranceClaim
            {
                Id = _nextClaimId++,
                PolicyId = policyId,
                RentalId = policy.RentalId,
                CustomerId = policy.CustomerId,
                ClaimType = claimType,
                Amount = approvedAmount,
                FiledAt = _clock.Now,
                Status = ClaimStatus.Approved
            };

            policy.TotalClaimed += approvedAmount;
            if (policy.TotalClaimed >= policy.CoverageLimit)
                policy.Status = InsurancePolicyStatus.Claimed;

            _claims.Add(claim);
            return claim;
        }

        /// <summary>
        /// Deny a pending claim with a reason.
        /// </summary>
        public InsuranceClaim DenyClaim(int claimId, string reason)
        {
            var claim = _claims.FirstOrDefault(c => c.Id == claimId);
            if (claim == null)
                throw new InvalidOperationException("Claim not found.");
            if (claim.Status != ClaimStatus.Pending && claim.Status != ClaimStatus.Approved)
                throw new InvalidOperationException("Claim cannot be denied in its current state.");

            // If it was approved, reverse the payout
            if (claim.Status == ClaimStatus.Approved)
            {
                var policy = _policies.First(p => p.Id == claim.PolicyId);
                policy.TotalClaimed -= claim.Amount;
                if (policy.Status == InsurancePolicyStatus.Claimed)
                    policy.Status = InsurancePolicyStatus.Active;
            }

            claim.Status = ClaimStatus.Denied;
            claim.DenialReason = reason ?? "No reason provided.";
            return claim;
        }

        // ── Query ────────────────────────────────────────────────────

        /// <summary>Get a policy by ID.</summary>
        public InsurancePolicy GetPolicy(int policyId)
        {
            return _policies.FirstOrDefault(p => p.Id == policyId);
        }

        /// <summary>Get the active policy for a rental, or null.</summary>
        public InsurancePolicy GetPolicyForRental(int rentalId)
        {
            return _policies.FirstOrDefault(p => p.RentalId == rentalId
                && p.Status == InsurancePolicyStatus.Active);
        }

        /// <summary>Get all policies for a customer.</summary>
        public List<InsurancePolicy> GetCustomerPolicies(int customerId)
        {
            return _policies.Where(p => p.CustomerId == customerId)
                            .OrderByDescending(p => p.PurchasedAt)
                            .ToList();
        }

        /// <summary>Get all claims for a policy.</summary>
        public List<InsuranceClaim> GetClaimsForPolicy(int policyId)
        {
            return _claims.Where(c => c.PolicyId == policyId)
                          .OrderByDescending(c => c.FiledAt)
                          .ToList();
        }

        /// <summary>Get all claims for a customer.</summary>
        public List<InsuranceClaim> GetCustomerClaims(int customerId)
        {
            return _claims.Where(c => c.CustomerId == customerId)
                          .OrderByDescending(c => c.FiledAt)
                          .ToList();
        }

        /// <summary>Cancel a policy (e.g. early return with no issues).</summary>
        public InsurancePolicy CancelPolicy(int policyId)
        {
            var policy = _policies.FirstOrDefault(p => p.Id == policyId);
            if (policy == null)
                throw new InvalidOperationException("Policy not found.");
            if (policy.Status != InsurancePolicyStatus.Active)
                throw new InvalidOperationException("Only active policies can be cancelled.");

            // Cannot cancel if claims have been paid
            var hasPaidClaims = _claims.Any(c => c.PolicyId == policyId
                && (c.Status == ClaimStatus.Approved || c.Status == ClaimStatus.Paid));
            if (hasPaidClaims)
                throw new InvalidOperationException("Cannot cancel policy with approved/paid claims.");

            policy.Status = InsurancePolicyStatus.Cancelled;
            return policy;
        }

        /// <summary>Expire a policy (rental returned, no more claims possible).</summary>
        public InsurancePolicy ExpirePolicy(int policyId)
        {
            var policy = _policies.FirstOrDefault(p => p.Id == policyId);
            if (policy == null)
                throw new InvalidOperationException("Policy not found.");

            if (policy.Status == InsurancePolicyStatus.Active)
                policy.Status = InsurancePolicyStatus.Expired;

            return policy;
        }

        // ── Analytics ────────────────────────────────────────────────

        /// <summary>
        /// Get comprehensive insurance analytics.
        /// </summary>
        public InsuranceAnalytics GetAnalytics()
        {
            var totalPremiums = _policies.Sum(p => p.Premium);
            var totalPayouts = _claims.Where(c => c.Status == ClaimStatus.Approved || c.Status == ClaimStatus.Paid)
                                      .Sum(c => c.Amount);

            var policiesByTier = new Dictionary<InsuranceTier, int>();
            foreach (InsuranceTier tier in Enum.GetValues(typeof(InsuranceTier)))
            {
                policiesByTier[tier] = _policies.Count(p => p.Tier == tier);
            }

            var claimsByType = new Dictionary<ClaimType, int>();
            foreach (ClaimType ct in Enum.GetValues(typeof(ClaimType)))
            {
                claimsByType[ct] = _claims.Count(c => c.ClaimType == ct);
            }

            var approvedClaims = _claims.Count(c => c.Status == ClaimStatus.Approved || c.Status == ClaimStatus.Paid);
            var deniedClaims = _claims.Count(c => c.Status == ClaimStatus.Denied);
            var totalClaims = _claims.Count;

            return new InsuranceAnalytics
            {
                TotalPolicies = _policies.Count,
                ActivePolicies = _policies.Count(p => p.Status == InsurancePolicyStatus.Active),
                TotalPremiums = totalPremiums,
                TotalPayouts = totalPayouts,
                ProfitMargin = totalPremiums > 0
                    ? Math.Round((totalPremiums - totalPayouts) / totalPremiums * 100, 1)
                    : 0,
                TotalClaims = totalClaims,
                ApprovedClaims = approvedClaims,
                DeniedClaims = deniedClaims,
                ApprovalRate = totalClaims > 0
                    ? Math.Round((decimal)approvedClaims / totalClaims * 100, 1)
                    : 0,
                AverageClaimAmount = approvedClaims > 0
                    ? Math.Round(totalPayouts / approvedClaims, 2)
                    : 0,
                PoliciesByTier = policiesByTier,
                ClaimsByType = claimsByType,
                LossRatio = totalPremiums > 0
                    ? Math.Round(totalPayouts / totalPremiums * 100, 1)
                    : 0
            };
        }

        /// <summary>
        /// Get the insurance uptake rate: % of rentals with policies.
        /// </summary>
        public decimal GetUptakeRate()
        {
            var totalRentals = _rentalRepo.GetAll().Count();
            if (totalRentals == 0) return 0;
            return Math.Round((decimal)_policies.Count / totalRentals * 100, 1);
        }

        /// <summary>
        /// Check if a customer is a frequent claimer (3+ claims).
        /// </summary>
        public bool IsFrequentClaimer(int customerId)
        {
            return _claims.Count(c => c.CustomerId == customerId
                && (c.Status == ClaimStatus.Approved || c.Status == ClaimStatus.Paid)) >= 3;
        }

        /// <summary>
        /// Get customers ranked by claim frequency (descending).
        /// </summary>
        public List<KeyValuePair<int, int>> GetTopClaimers(int limit = 10)
        {
            return _claims
                .Where(c => c.Status == ClaimStatus.Approved || c.Status == ClaimStatus.Paid)
                .GroupBy(c => c.CustomerId)
                .Select(g => new KeyValuePair<int, int>(g.Key, g.Count()))
                .OrderByDescending(kv => kv.Value)
                .Take(limit)
                .ToList();
        }

        /// <summary>
        /// Reset all data (for testing).
        /// </summary>
        public void Reset()
        {
            _policies.Clear();
            _claims.Clear();
            _nextPolicyId = 1;
            _nextClaimId = 1;
        }
    }
}
