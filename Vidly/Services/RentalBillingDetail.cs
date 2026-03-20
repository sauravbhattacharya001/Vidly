using System;

namespace Vidly.Services
{
    /// <summary>
    /// Billing detail for a single active rental.
    /// </summary>
    public class RentalBillingDetail
    {
        public int RentalId { get; set; }
        public string MovieName { get; set; }
        public DateTime RentalDate { get; set; }
        public DateTime DueDate { get; set; }
        public decimal DailyRate { get; set; }
        public bool IsOverdue { get; set; }
        public int DaysOverdue { get; set; }
        public decimal ProjectedLateFee { get; set; }
        public string LateFeeExplanation { get; set; }
    }
}
