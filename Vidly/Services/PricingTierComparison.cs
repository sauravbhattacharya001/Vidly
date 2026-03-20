using Vidly.Models;

namespace Vidly.Services
{
    /// <summary>
    /// Cost comparison across membership tiers.
    /// </summary>
    public class PricingTierComparison
    {
        public MembershipType Tier { get; set; }
        public decimal DailyRate { get; set; }
        public int RentalDays { get; set; }
        public decimal TotalCost { get; set; }
        public decimal Savings { get; set; }
        public int GracePeriodDays { get; set; }
        public int FreeRentalsPerMonth { get; set; }
        public int LateFeeReduction { get; set; }
    }
}
