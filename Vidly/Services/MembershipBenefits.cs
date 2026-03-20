using Vidly.Models;

namespace Vidly.Services
{
    /// <summary>
    /// Benefits associated with a membership tier.
    /// </summary>
    public class MembershipBenefits
    {
        public MembershipType Tier { get; set; }
        public int DiscountPercent { get; set; }
        public int GracePeriodDays { get; set; }
        public int MaxConcurrentRentals { get; set; }
        public int FreeRentalsPerMonth { get; set; }
        public int LateFeeDiscount { get; set; }
        public int ExtendedRentalDays { get; set; }
        public string Description { get; set; }
    }
}
