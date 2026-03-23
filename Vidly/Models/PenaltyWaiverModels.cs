using System;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// Represents a late-fee waiver granted on an overdue rental.
    /// </summary>
    public class PenaltyWaiver
    {
        public int Id { get; set; }

        [Required]
        [Display(Name = "Rental")]
        public int RentalId { get; set; }

        /// <summary>Customer name (resolved from rental).</summary>
        public string CustomerName { get; set; }

        /// <summary>Movie name (resolved from rental).</summary>
        public string MovieName { get; set; }

        /// <summary>Original late fee before waiver.</summary>
        [Display(Name = "Original Late Fee")]
        [DataType(DataType.Currency)]
        public decimal OriginalLateFee { get; set; }

        /// <summary>Amount waived (may be partial).</summary>
        [Required]
        [Display(Name = "Amount Waived")]
        [Range(0.01, 99999.99, ErrorMessage = "Waiver amount must be positive.")]
        [DataType(DataType.Currency)]
        public decimal AmountWaived { get; set; }

        /// <summary>Remaining fee after waiver.</summary>
        [Display(Name = "Remaining Fee")]
        [DataType(DataType.Currency)]
        public decimal RemainingFee => Math.Max(0, OriginalLateFee - AmountWaived);

        [Required(ErrorMessage = "Please provide a reason for the waiver.")]
        [StringLength(500)]
        [Display(Name = "Reason")]
        public string Reason { get; set; }

        [Display(Name = "Waiver Type")]
        public WaiverType Type { get; set; }

        [Display(Name = "Granted On")]
        [DataType(DataType.Date)]
        public DateTime GrantedDate { get; set; }

        /// <summary>Staff member who approved (placeholder name).</summary>
        [Display(Name = "Approved By")]
        public string ApprovedBy { get; set; }
    }

    public enum WaiverType
    {
        [Display(Name = "Full Waiver")]
        Full = 1,

        [Display(Name = "Partial Waiver")]
        Partial = 2,

        [Display(Name = "Goodwill Gesture")]
        Goodwill = 3,

        [Display(Name = "System Error")]
        SystemError = 4,

        [Display(Name = "First Offense")]
        FirstOffense = 5
    }
}
