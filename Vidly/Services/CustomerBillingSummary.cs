using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Services
{
    /// <summary>
    /// Customer billing summary.
    /// </summary>
    public class CustomerBillingSummary
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public MembershipType MembershipTier { get; set; }
        public MembershipBenefits Benefits { get; set; }
        public int ActiveRentalCount { get; set; }
        public int OverdueRentalCount { get; set; }
        public int MaxConcurrentRentals { get; set; }
        public bool CanRentMore { get; set; }
        public int RemainingSlots { get; set; }
        public decimal TotalProjectedLateFees { get; set; }
        public decimal LifetimeSpend { get; set; }
        public List<RentalBillingDetail> Rentals { get; set; }
        public int FreeRentalsUsedThisMonth { get; set; }
        public int FreeRentalsRemaining { get; set; }
    }
}
