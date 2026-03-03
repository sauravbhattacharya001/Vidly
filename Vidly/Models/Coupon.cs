using System;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// Represents a promotional coupon that can be applied at checkout
    /// to discount a rental.
    /// </summary>
    public class Coupon
    {
        public int Id { get; set; }

        /// <summary>
        /// The coupon code customers enter (e.g., "WELCOME20", "SUMMER50").
        /// Case-insensitive matching is enforced at the repository level.
        /// </summary>
        [Required(ErrorMessage = "Coupon code is required.")]
        [StringLength(30, MinimumLength = 3, ErrorMessage = "Code must be 3–30 characters.")]
        [RegularExpression(@"^[A-Z0-9\-]+$", ErrorMessage = "Code must contain only uppercase letters, digits, and hyphens.")]
        [Display(Name = "Coupon Code")]
        public string Code { get; set; }

        /// <summary>
        /// Human-readable description of the promotion.
        /// </summary>
        [Required(ErrorMessage = "Description is required.")]
        [StringLength(200)]
        public string Description { get; set; }

        /// <summary>
        /// Type of discount: percentage off or fixed dollar amount.
        /// </summary>
        [Display(Name = "Discount Type")]
        public DiscountType DiscountType { get; set; }

        /// <summary>
        /// The discount value — percentage (1–100) or dollar amount.
        /// </summary>
        [Required]
        [Range(0.01, 100, ErrorMessage = "Value must be between 0.01 and 100.")]
        [Display(Name = "Discount Value")]
        public decimal DiscountValue { get; set; }

        /// <summary>
        /// Minimum rental subtotal required to use this coupon.
        /// </summary>
        [Display(Name = "Minimum Order ($)")]
        [Range(0, 9999)]
        public decimal MinimumOrderAmount { get; set; }

        /// <summary>
        /// Maximum discount that can be applied (for percentage coupons).
        /// Null means no cap.
        /// </summary>
        [Display(Name = "Max Discount ($)")]
        [Range(0.01, 9999)]
        public decimal? MaxDiscountAmount { get; set; }

        /// <summary>
        /// When the coupon becomes valid.
        /// </summary>
        [Required]
        [Display(Name = "Valid From")]
        [DataType(DataType.Date)]
        public DateTime ValidFrom { get; set; }

        /// <summary>
        /// When the coupon expires.
        /// </summary>
        [Required]
        [Display(Name = "Valid Until")]
        [DataType(DataType.Date)]
        public DateTime ValidUntil { get; set; }

        /// <summary>
        /// Maximum number of times this coupon can be redeemed across all customers.
        /// Null means unlimited.
        /// </summary>
        [Display(Name = "Max Redemptions")]
        [Range(1, 999999)]
        public int? MaxRedemptions { get; set; }

        /// <summary>
        /// How many times this coupon has been used.
        /// </summary>
        [Display(Name = "Times Used")]
        public int TimesUsed { get; set; }

        /// <summary>
        /// Whether this coupon is currently active (can be toggled by admin).
        /// </summary>
        [Display(Name = "Active")]
        public bool IsActive { get; set; }

        /// <summary>
        /// When the coupon was created.
        /// </summary>
        [Display(Name = "Created")]
        public DateTime CreatedDate { get; set; }

        /// <summary>
        /// Whether the coupon is currently valid for use.
        /// </summary>
        public bool IsValid =>
            IsActive
            && DateTime.Today >= ValidFrom
            && DateTime.Today <= ValidUntil
            && (!MaxRedemptions.HasValue || TimesUsed < MaxRedemptions.Value);

        /// <summary>
        /// Remaining uses, or null if unlimited.
        /// </summary>
        public int? RemainingUses =>
            MaxRedemptions.HasValue ? MaxRedemptions.Value - TimesUsed : (int?)null;

        /// <summary>
        /// Friendly status string for display.
        /// </summary>
        public string StatusDisplay
        {
            get
            {
                if (!IsActive) return "Disabled";
                if (DateTime.Today < ValidFrom) return "Scheduled";
                if (DateTime.Today > ValidUntil) return "Expired";
                if (MaxRedemptions.HasValue && TimesUsed >= MaxRedemptions.Value) return "Exhausted";
                return "Active";
            }
        }
    }

    /// <summary>
    /// Type of coupon discount.
    /// </summary>
    public enum DiscountType
    {
        [Display(Name = "Percentage Off")]
        Percentage = 1,

        [Display(Name = "Fixed Amount Off")]
        FixedAmount = 2
    }
}
