namespace Vidly.Services
{
    /// <summary>
    /// Single source of truth for rental policy constants shared across
    /// <see cref="PricingService"/>, <see cref="RentalReturnService"/>,
    /// and <see cref="LoyaltyPointsService"/>.
    /// <para>
    /// Previously these values were independently declared in each service
    /// with identical values, creating a divergence risk if one service
    /// updated a constant without the others.
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
    }
}
