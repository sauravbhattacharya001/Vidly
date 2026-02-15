using System;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// Represents a movie rental transaction linking a customer to a movie.
    /// </summary>
    public class Rental
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Customer is required.")]
        [Display(Name = "Customer")]
        public int CustomerId { get; set; }

        /// <summary>
        /// Resolved customer name (read-only, populated by repository).
        /// </summary>
        public string CustomerName { get; set; }

        [Required(ErrorMessage = "Movie is required.")]
        [Display(Name = "Movie")]
        public int MovieId { get; set; }

        /// <summary>
        /// Resolved movie name (read-only, populated by repository).
        /// </summary>
        public string MovieName { get; set; }

        [Required(ErrorMessage = "Rental date is required.")]
        [Display(Name = "Rented On")]
        [DataType(DataType.Date)]
        public DateTime RentalDate { get; set; }

        [Display(Name = "Due Date")]
        [DataType(DataType.Date)]
        public DateTime DueDate { get; set; }

        [Display(Name = "Returned On")]
        [DataType(DataType.Date)]
        public DateTime? ReturnDate { get; set; }

        [Display(Name = "Daily Rate")]
        [Range(0.01, 999.99, ErrorMessage = "Daily rate must be between $0.01 and $999.99.")]
        [DataType(DataType.Currency)]
        public decimal DailyRate { get; set; }

        [Display(Name = "Late Fee")]
        [DataType(DataType.Currency)]
        public decimal LateFee { get; set; }

        /// <summary>
        /// Current status of the rental.
        /// </summary>
        [Display(Name = "Status")]
        public RentalStatus Status { get; set; }

        /// <summary>
        /// Total cost = (rental days * daily rate) + late fee.
        /// </summary>
        [Display(Name = "Total Cost")]
        public decimal TotalCost
        {
            get
            {
                var endDate = ReturnDate ?? DateTime.Today;
                var days = Math.Max(1, (int)(endDate - RentalDate).TotalDays);
                return (days * DailyRate) + LateFee;
            }
        }

        /// <summary>
        /// Number of days overdue (0 if not overdue).
        /// </summary>
        public int DaysOverdue
        {
            get
            {
                if (Status == RentalStatus.Returned)
                    return ReturnDate.HasValue && ReturnDate.Value > DueDate
                        ? (int)(ReturnDate.Value - DueDate).TotalDays
                        : 0;

                return DateTime.Today > DueDate
                    ? (int)(DateTime.Today - DueDate).TotalDays
                    : 0;
            }
        }

        /// <summary>
        /// Whether this rental is currently overdue.
        /// </summary>
        public bool IsOverdue =>
            Status != RentalStatus.Returned && DateTime.Today > DueDate;
    }

    /// <summary>
    /// Rental lifecycle status.
    /// </summary>
    public enum RentalStatus
    {
        [Display(Name = "Active")]
        Active = 1,

        [Display(Name = "Overdue")]
        Overdue = 2,

        [Display(Name = "Returned")]
        Returned = 3
    }
}
