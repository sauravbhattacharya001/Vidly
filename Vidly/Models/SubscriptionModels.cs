using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// Available subscription plan types with increasing rental allowances.
    /// </summary>
    public enum SubscriptionPlanType
    {
        /// <summary>2 rentals per month.</summary>
        Basic,

        /// <summary>5 rentals per month.</summary>
        Standard,

        /// <summary>Unlimited rentals per month.</summary>
        Premium
    }

    /// <summary>
    /// Current status of a customer's subscription.
    /// </summary>
    public enum SubscriptionStatus
    {
        Active,
        Paused,
        Cancelled,
        Expired,
        PastDue
    }

    /// <summary>
    /// Defines a subscription plan's features and pricing.
    /// </summary>
    public class SubscriptionPlan
    {
        public SubscriptionPlanType PlanType { get; set; }

        [Display(Name = "Plan Name")]
        public string Name { get; set; }

        [Display(Name = "Monthly Price")]
        [DataType(DataType.Currency)]
        public decimal MonthlyPrice { get; set; }

        /// <summary>
        /// Max rentals per billing cycle. 0 means unlimited.
        /// </summary>
        [Display(Name = "Rentals Per Month")]
        public int RentalsPerMonth { get; set; }

        /// <summary>Discount % on per-rental fees beyond plan allowance.</summary>
        public int OverageDiscountPercent { get; set; }

        /// <summary>Whether the plan includes free reservations.</summary>
        public bool FreeReservations { get; set; }

        /// <summary>Grace days added to all rentals (before late fees).</summary>
        public int ExtraGraceDays { get; set; }

        /// <summary>Maximum number of times a subscription can be paused.</summary>
        public int MaxPausesPerYear { get; set; }
    }

    /// <summary>
    /// A customer's active or historical subscription.
    /// </summary>
    public class CustomerSubscription
    {
        public int Id { get; set; }

        [Required]
        public int CustomerId { get; set; }

        [Required]
        public SubscriptionPlanType PlanType { get; set; }

        public SubscriptionStatus Status { get; set; }

        [Display(Name = "Start Date")]
        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; }

        /// <summary>Current billing cycle start.</summary>
        [Display(Name = "Current Period Start")]
        [DataType(DataType.Date)]
        public DateTime CurrentPeriodStart { get; set; }

        /// <summary>Current billing cycle end.</summary>
        [Display(Name = "Current Period End")]
        [DataType(DataType.Date)]
        public DateTime CurrentPeriodEnd { get; set; }

        /// <summary>When the subscription was cancelled (null if active).</summary>
        [DataType(DataType.Date)]
        public DateTime? CancelledDate { get; set; }

        /// <summary>When the current pause started (null if not paused).</summary>
        [DataType(DataType.Date)]
        public DateTime? PausedDate { get; set; }

        /// <summary>Number of times paused in the current calendar year.</summary>
        public int PausesUsedThisYear { get; set; }

        /// <summary>Rentals used in the current billing period.</summary>
        public int RentalsUsedThisPeriod { get; set; }

        /// <summary>Total amount billed over the lifetime of this subscription.</summary>
        [DataType(DataType.Currency)]
        public decimal TotalBilled { get; set; }

        /// <summary>Billing transaction history.</summary>
        public List<SubscriptionBillingEvent> BillingHistory { get; set; }

        public CustomerSubscription()
        {
            BillingHistory = new List<SubscriptionBillingEvent>();
            Status = SubscriptionStatus.Active;
        }

        /// <summary>Whether the subscriber has remaining rentals this period.</summary>
        public bool HasRentalsRemaining(int planLimit)
        {
            if (planLimit == 0) return true; // unlimited
            return RentalsUsedThisPeriod < planLimit;
        }

        /// <summary>Whether the subscription is in a billable state.</summary>
        public bool IsBillable
        {
            get
            {
                return Status == SubscriptionStatus.Active
                    || Status == SubscriptionStatus.PastDue;
            }
        }
    }

    /// <summary>
    /// A billing event on a subscription (charge, refund, plan change).
    /// </summary>
    public class SubscriptionBillingEvent
    {
        public int Id { get; set; }

        public int SubscriptionId { get; set; }

        [Required]
        public string EventType { get; set; } // "charge", "refund", "plan_change", "pause", "resume"

        [DataType(DataType.Currency)]
        public decimal Amount { get; set; }

        public DateTime Timestamp { get; set; }

        public string Description { get; set; }

        public SubscriptionBillingEvent()
        {
            Timestamp = DateTime.Now;
        }
    }
}
