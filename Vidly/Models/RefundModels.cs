using System;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// Represents a refund request for a rental transaction.
    /// </summary>
    public class RefundRequest
    {
        public int Id { get; set; }

        [Required]
        [Display(Name = "Rental")]
        public int RentalId { get; set; }

        [Required]
        [Display(Name = "Customer")]
        public int CustomerId { get; set; }

        [Display(Name = "Customer Name")]
        public string CustomerName { get; set; }

        [Display(Name = "Movie")]
        public string MovieName { get; set; }

        [Required]
        [Display(Name = "Reason")]
        public RefundReason Reason { get; set; }

        [Display(Name = "Details")]
        [StringLength(500)]
        public string Details { get; set; }

        [Display(Name = "Requested On")]
        [DataType(DataType.DateTime)]
        public DateTime RequestedDate { get; set; }

        [Display(Name = "Resolved On")]
        [DataType(DataType.DateTime)]
        public DateTime? ResolvedDate { get; set; }

        [Display(Name = "Status")]
        public RefundStatus Status { get; set; }

        [Display(Name = "Original Amount")]
        [DataType(DataType.Currency)]
        public decimal OriginalAmount { get; set; }

        [Display(Name = "Refund Amount")]
        [DataType(DataType.Currency)]
        public decimal RefundAmount { get; set; }

        [Display(Name = "Refund Type")]
        public RefundType Type { get; set; }

        [Display(Name = "Staff Notes")]
        [StringLength(500)]
        public string StaffNotes { get; set; }
    }

    public enum RefundReason
    {
        [Display(Name = "Defective Disc")]
        DefectiveDisc = 1,

        [Display(Name = "Wrong Movie")]
        WrongMovie = 2,

        [Display(Name = "Billing Error")]
        BillingError = 3,

        [Display(Name = "Poor Quality")]
        PoorQuality = 4,

        [Display(Name = "Service Issue")]
        ServiceIssue = 5,

        [Display(Name = "Other")]
        Other = 6
    }

    public enum RefundStatus
    {
        [Display(Name = "Pending")]
        Pending = 1,

        [Display(Name = "Approved")]
        Approved = 2,

        [Display(Name = "Denied")]
        Denied = 3,

        [Display(Name = "Processed")]
        Processed = 4
    }

    public enum RefundType
    {
        [Display(Name = "Full Refund")]
        Full = 1,

        [Display(Name = "Partial Refund")]
        Partial = 2,

        [Display(Name = "Store Credit")]
        StoreCredit = 3
    }
}
