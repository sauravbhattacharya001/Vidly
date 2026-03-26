using System;

namespace Vidly.Models
{
    /// <summary>
    /// Represents a printable receipt for a returned rental.
    /// </summary>
    public class ReturnReceipt
    {
        public string ReceiptNumber { get; set; }
        public DateTime GeneratedAt { get; set; }

        // Rental details
        public int RentalId { get; set; }
        public string CustomerName { get; set; }
        public string MovieName { get; set; }
        public DateTime RentalDate { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime ReturnDate { get; set; }

        // Financial details
        public decimal DailyRate { get; set; }
        public int RentalDays { get; set; }
        public decimal RentalCost { get; set; }
        public int DaysLate { get; set; }
        public decimal LateFee { get; set; }
        public decimal TotalCharge { get; set; }

        // Status
        public bool WasLate { get; set; }
        public bool WasOnTime => !WasLate;
    }
}
