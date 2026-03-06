using System;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// A customer dispute against a rental charge (late fee, damage, or overcharge).
    /// Tracks the full lifecycle from submission through review to resolution.
    /// </summary>
    public class Dispute
    {
        public int Id { get; set; }

        [Required]
        public int RentalId { get; set; }

        [Required]
        public int CustomerId { get; set; }

        /// <summary>Resolved customer name (read-only, populated by repository).</summary>
        public string CustomerName { get; set; }

        /// <summary>Resolved movie name (read-only, populated by repository).</summary>
        public string MovieName { get; set; }

        [Required]
        public DisputeType Type { get; set; }

        [Required]
        [StringLength(1000, MinimumLength = 10,
            ErrorMessage = "Reason must be between 10 and 1000 characters.")]
        public string Reason { get; set; }

        /// <summary>The dollar amount being disputed.</summary>
        [Range(0.01, 9999.99)]
        [DataType(DataType.Currency)]
        public decimal DisputedAmount { get; set; }

        /// <summary>Amount refunded if partially or fully approved.</summary>
        [DataType(DataType.Currency)]
        public decimal RefundAmount { get; set; }

        public DisputeStatus Status { get; set; } = DisputeStatus.Open;

        [DataType(DataType.Date)]
        public DateTime SubmittedDate { get; set; } = DateTime.Today;

        [DataType(DataType.Date)]
        public DateTime? ResolvedDate { get; set; }

        /// <summary>Staff notes or auto-resolution explanation.</summary>
        [StringLength(1000)]
        public string ResolutionNotes { get; set; }

        /// <summary>Who or what resolved it (staff name or "Auto").</summary>
        [StringLength(100)]
        public string ResolvedBy { get; set; }

        /// <summary>Priority based on customer tier and dispute amount.</summary>
        public DisputePriority Priority { get; set; } = DisputePriority.Normal;

        /// <summary>Days since submission.</summary>
        public int AgeDays => (DateTime.Today - SubmittedDate).Days;

        /// <summary>Whether this dispute is still actionable.</summary>
        public bool IsOpen => Status == DisputeStatus.Open
                           || Status == DisputeStatus.UnderReview;
    }

    public enum DisputeType
    {
        [Display(Name = "Late Fee")]
        LateFee = 1,

        [Display(Name = "Damage Charge")]
        DamageCharge = 2,

        [Display(Name = "Overcharge")]
        Overcharge = 3,

        [Display(Name = "Wrong Movie")]
        WrongMovie = 4,

        [Display(Name = "Other")]
        Other = 5
    }

    public enum DisputeStatus
    {
        [Display(Name = "Open")]
        Open = 1,

        [Display(Name = "Under Review")]
        UnderReview = 2,

        [Display(Name = "Approved")]
        Approved = 3,

        [Display(Name = "Partially Approved")]
        PartiallyApproved = 4,

        [Display(Name = "Denied")]
        Denied = 5,

        [Display(Name = "Expired")]
        Expired = 6
    }

    public enum DisputePriority
    {
        Low = 1,
        Normal = 2,
        High = 3,
        Urgent = 4
    }
}
