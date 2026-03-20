using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// A consolidated rental statement for a customer over a date range.
    /// </summary>
    public class CustomerStatement
    {
        public Customer Customer { get; set; }

        [Display(Name = "Statement Period Start")]
        [DataType(DataType.Date)]
        public DateTime PeriodStart { get; set; }

        [Display(Name = "Statement Period End")]
        [DataType(DataType.Date)]
        public DateTime PeriodEnd { get; set; }

        public DateTime GeneratedAt { get; set; }

        public string StatementNumber { get; set; }

        public IReadOnlyList<StatementLineItem> LineItems { get; set; }

        public decimal Subtotal { get; set; }

        public decimal TotalLateFees { get; set; }

        public decimal GrandTotal { get; set; }

        public int TotalRentals { get; set; }

        public int ActiveRentals { get; set; }

        public int ReturnedRentals { get; set; }

        public int OverdueRentals { get; set; }

        public double AverageDurationDays { get; set; }
    }

    /// <summary>
    /// A single line item on a customer statement.
    /// </summary>
    public class StatementLineItem
    {
        public int RentalId { get; set; }

        public string MovieName { get; set; }

        [DataType(DataType.Date)]
        public DateTime RentalDate { get; set; }

        [DataType(DataType.Date)]
        public DateTime DueDate { get; set; }

        [DataType(DataType.Date)]
        public DateTime? ReturnDate { get; set; }

        public int DaysRented { get; set; }

        public decimal DailyRate { get; set; }

        public decimal RentalCost { get; set; }

        public decimal LateFee { get; set; }

        public decimal LineTotal { get; set; }

        public RentalStatus Status { get; set; }

        public bool WasLate { get; set; }
    }
}
