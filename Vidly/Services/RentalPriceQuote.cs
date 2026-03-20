using System;
using Vidly.Models;

namespace Vidly.Services
{
    /// <summary>
    /// Detailed rental price quote.
    /// </summary>
    public class RentalPriceQuote
    {
        public string MovieName { get; set; }
        public string CustomerName { get; set; }
        public MembershipType MembershipTier { get; set; }
        public decimal BaseDailyRate { get; set; }
        public int DiscountPercent { get; set; }
        public decimal DiscountedDailyRate { get; set; }
        public int RentalDays { get; set; }
        public DateTime DueDate { get; set; }
        public decimal SubTotal { get; set; }
        public bool FreeRentalApplied { get; set; }
        public decimal FinalTotal { get; set; }
        public int GracePeriodDays { get; set; }
        public decimal Savings { get; set; }
    }
}
