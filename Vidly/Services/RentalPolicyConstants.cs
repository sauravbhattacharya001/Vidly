using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Services
{
    /// <summary>
    /// Single source of truth for rental policy constants shared across
    /// <see cref="PricingService"/>, <see cref="RentalReturnService"/>,
    /// and <see cref="LoyaltyPointsService"/>.
    /// <para>
    /// Previously these values were independently declared in each service
    /// with identical values, creating a divergence risk if one service
    /// updated a constant without the others.  Tier-specific policy data
    /// (loyalty multipliers, late-fee discounts) is also centralized here
    /// to eliminate parallel switch statements.
    /// </para>
    /// </summary>
    public static class RentalPolicyConstants
    {
        // ── Late-fee policy ──────────────────────────────────────────

        /// <summary>Per-day late fee (before tier discount).</summary>
        public const decimal LateFeePerDay = 1.50m;

        /// <summary>Maximum late fee on any single rental.</summary>
        public const decimal MaxLateFeeCap = 25.00m;

        // ── Loyalty / return bonuses ─────────────────────────────────

        /// <summary>Bonus loyalty points for an on-time or early return.</summary>
        public const int OnTimeReturnBonus = 25;

        // ── Tier-specific policy data ────────────────────────────────

        /// <summary>
        /// Loyalty points multiplier by membership tier.
        /// Higher tiers earn points faster.
        /// </summary>
        public static readonly IReadOnlyDictionary<MembershipType, decimal> TierPointsMultiplier
            = new Dictionary<MembershipType, decimal>
            {
                [MembershipType.Basic]    = 1.0m,
                [MembershipType.Silver]   = 1.25m,
                [MembershipType.Gold]     = 1.5m,
                [MembershipType.Platinum] = 2.0m,
            };

        /// <summary>
        /// Late-fee discount percentage by membership tier.
        /// Applied after the base per-day fee is calculated.
        /// </summary>
        public static readonly IReadOnlyDictionary<MembershipType, decimal> TierLateFeeDiscount
            = new Dictionary<MembershipType, decimal>
            {
                [MembershipType.Basic]    = 0m,
                [MembershipType.Silver]   = 0.10m,
                [MembershipType.Gold]     = 0.25m,
                [MembershipType.Platinum] = 0.50m,
            };

        /// <summary>
        /// Additional return grace-period days beyond the base (1 day)
        /// by membership tier.  The total grace period is
        /// <c>BaseReturnGracePeriodDays + TierExtraGraceDays[tier]</c>.
        /// </summary>
        public const int BaseReturnGracePeriodDays = 1;

        /// <summary>Extra grace days on top of <see cref="BaseReturnGracePeriodDays"/>.</summary>
        public static readonly IReadOnlyDictionary<MembershipType, int> TierExtraGraceDays
            = new Dictionary<MembershipType, int>
            {
                [MembershipType.Basic]    = 0,
                [MembershipType.Silver]   = 1,
                [MembershipType.Gold]     = 2,
                [MembershipType.Platinum] = 4,
            };

        /// <summary>
        /// Look up a tier value with a safe fallback for unknown enum values.
        /// </summary>
        public static T GetTierValue<T>(IReadOnlyDictionary<MembershipType, T> table,
                                         MembershipType tier, T fallback)
        {
            return table.TryGetValue(tier, out var value) ? value : fallback;
        }
    }
}
