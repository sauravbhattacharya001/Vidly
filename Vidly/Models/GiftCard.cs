using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// Represents a gift card with a monetary balance that can be
    /// purchased, gifted, and redeemed against rentals.
    /// </summary>
    public class GiftCard
    {
        public int Id { get; set; }

        /// <summary>
        /// Unique 16-character alphanumeric code (e.g., "GIFT-XXXX-XXXX-XXXX").
        /// </summary>
        [Required]
        [StringLength(19)]
        [Display(Name = "Card Code")]
        public string Code { get; set; }

        /// <summary>
        /// The original value loaded onto the card.
        /// </summary>
        [Required]
        [Range(5.00, 500.00, ErrorMessage = "Gift card value must be between $5 and $500.")]
        [Display(Name = "Original Value")]
        [DataType(DataType.Currency)]
        public decimal OriginalValue { get; set; }

        /// <summary>
        /// Current remaining balance.
        /// </summary>
        [Display(Name = "Balance")]
        [DataType(DataType.Currency)]
        public decimal Balance { get; set; }

        /// <summary>
        /// Name of the person who purchased/created the card.
        /// </summary>
        [StringLength(100)]
        [Display(Name = "Purchased By")]
        public string PurchaserName { get; set; }

        /// <summary>
        /// Optional recipient name for gifting.
        /// </summary>
        [StringLength(100)]
        [Display(Name = "Recipient")]
        public string RecipientName { get; set; }

        /// <summary>
        /// Optional personal message.
        /// </summary>
        [StringLength(500)]
        [Display(Name = "Message")]
        public string Message { get; set; }

        /// <summary>
        /// Whether the card is active and can be redeemed.
        /// </summary>
        [Display(Name = "Active")]
        public bool IsActive { get; set; }

        /// <summary>
        /// Optional expiration date. Null means no expiration.
        /// </summary>
        [Display(Name = "Expires")]
        [DataType(DataType.Date)]
        public DateTime? ExpirationDate { get; set; }

        /// <summary>
        /// When the card was created.
        /// </summary>
        [Display(Name = "Created")]
        public DateTime CreatedDate { get; set; }

        /// <summary>
        /// Transaction history for this card.
        /// </summary>
        public List<GiftCardTransaction> Transactions { get; set; } = new List<GiftCardTransaction>();

        /// <summary>
        /// Whether the card can currently be redeemed.
        /// </summary>
        public bool IsRedeemable =>
            IsActive
            && Balance > 0
            && (!ExpirationDate.HasValue || DateTime.Today <= ExpirationDate.Value);

        /// <summary>
        /// Friendly status for display.
        /// </summary>
        public string StatusDisplay
        {
            get
            {
                if (!IsActive) return "Disabled";
                if (ExpirationDate.HasValue && DateTime.Today > ExpirationDate.Value) return "Expired";
                if (Balance <= 0) return "Empty";
                return "Active";
            }
        }
    }

    /// <summary>
    /// A single transaction against a gift card (purchase, redemption, refund).
    /// </summary>
    public class GiftCardTransaction
    {
        public int Id { get; set; }
        public int GiftCardId { get; set; }

        [Display(Name = "Type")]
        public GiftCardTransactionType Type { get; set; }

        [DataType(DataType.Currency)]
        public decimal Amount { get; set; }

        /// <summary>
        /// Balance after this transaction.
        /// </summary>
        [DataType(DataType.Currency)]
        [Display(Name = "Balance After")]
        public decimal BalanceAfter { get; set; }

        [StringLength(200)]
        public string Description { get; set; }

        public DateTime Date { get; set; }
    }

    public enum GiftCardTransactionType
    {
        [Display(Name = "Initial Load")]
        InitialLoad = 1,

        [Display(Name = "Redemption")]
        Redemption = 2,

        [Display(Name = "Refund")]
        Refund = 3,

        [Display(Name = "Top-Up")]
        TopUp = 4
    }
}
