using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Vidly.Models
{
    /// <summary>
    /// Late fee calculation strategy.
    /// </summary>
    public enum LateFeeStrategy
    {
        [Display(Name = "Flat Fee")]
        FlatFee = 1,

        [Display(Name = "Per Day")]
        PerDay = 2,

        [Display(Name = "Tiered (Graduated)")]
        Tiered = 3
    }

    /// <summary>
    /// A store-wide late fee policy that defines how fees are calculated.
    /// </summary>
    public class LateFeePolicy
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        [Display(Name = "Policy Name")]
        public string Name { get; set; }

        [Display(Name = "Strategy")]
        public LateFeeStrategy Strategy { get; set; }

        /// <summary>Flat fee amount (used when Strategy = FlatFee).</summary>
        [Display(Name = "Flat Fee ($)")]
        [Range(0, 999.99)]
        [DataType(DataType.Currency)]
        public decimal FlatFeeAmount { get; set; }

        /// <summary>Per-day fee (used when Strategy = PerDay or as base for Tiered).</summary>
        [Display(Name = "Per-Day Rate ($)")]
        [Range(0, 99.99)]
        [DataType(DataType.Currency)]
        public decimal PerDayRate { get; set; }

        /// <summary>Days after due date before fees kick in.</summary>
        [Display(Name = "Grace Period (days)")]
        [Range(0, 30)]
        public int GracePeriodDays { get; set; }

        /// <summary>Maximum fee cap (0 = no cap).</summary>
        [Display(Name = "Max Fee Cap ($)")]
        [Range(0, 9999.99)]
        [DataType(DataType.Currency)]
        public decimal MaxFeeCap { get; set; }

        /// <summary>Tier definitions for graduated fees.</summary>
        public List<LateFeeTier> Tiers { get; set; } = new List<LateFeeTier>();

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Calculate the late fee for a given number of overdue days.
        /// </summary>
        public decimal Calculate(int daysOverdue)
        {
            if (daysOverdue <= 0) return 0m;

            // Apply grace period
            var chargeableDays = Math.Max(0, daysOverdue - GracePeriodDays);
            if (chargeableDays <= 0) return 0m;

            decimal fee;

            switch (Strategy)
            {
                case LateFeeStrategy.FlatFee:
                    fee = FlatFeeAmount;
                    break;

                case LateFeeStrategy.PerDay:
                    fee = chargeableDays * PerDayRate;
                    break;

                case LateFeeStrategy.Tiered:
                    fee = CalculateTiered(chargeableDays);
                    break;

                default:
                    fee = chargeableDays * PerDayRate;
                    break;
            }

            if (MaxFeeCap > 0)
                fee = Math.Min(fee, MaxFeeCap);

            return Math.Round(fee, 2);
        }

        private decimal CalculateTiered(int days)
        {
            if (Tiers == null || !Tiers.Any())
                return days * PerDayRate;

            var sortedTiers = Tiers.OrderBy(t => t.FromDay).ToList();
            decimal total = 0m;
            int remaining = days;

            foreach (var tier in sortedTiers)
            {
                if (remaining <= 0) break;

                int tierDays = tier.ToDay.HasValue
                    ? Math.Min(remaining, tier.ToDay.Value - tier.FromDay + 1)
                    : remaining;

                total += tierDays * tier.RatePerDay;
                remaining -= tierDays;
            }

            // Any remaining days beyond last tier use the last tier's rate
            if (remaining > 0 && sortedTiers.Any())
                total += remaining * sortedTiers.Last().RatePerDay;

            return total;
        }
    }

    /// <summary>
    /// A tier in a graduated late fee schedule.
    /// </summary>
    public class LateFeeTier
    {
        public int Id { get; set; }
        public int PolicyId { get; set; }

        [Display(Name = "From Day")]
        [Range(1, 365)]
        public int FromDay { get; set; }

        [Display(Name = "To Day")]
        [Range(1, 365)]
        public int? ToDay { get; set; }

        [Display(Name = "Rate Per Day ($)")]
        [Range(0.01, 99.99)]
        [DataType(DataType.Currency)]
        public decimal RatePerDay { get; set; }
    }

    /// <summary>
    /// Result of a late fee calculation for display.
    /// </summary>
    public class LateFeeEstimate
    {
        public string PolicyName { get; set; }
        public LateFeeStrategy Strategy { get; set; }
        public int DaysOverdue { get; set; }
        public int GracePeriodDays { get; set; }
        public int ChargeableDays { get; set; }
        public decimal Fee { get; set; }
        public decimal MaxCap { get; set; }
        public bool WasCapped { get; set; }
        public List<TierBreakdown> TierBreakdowns { get; set; } = new List<TierBreakdown>();
    }

    public class TierBreakdown
    {
        public string TierLabel { get; set; }
        public int Days { get; set; }
        public decimal Rate { get; set; }
        public decimal Subtotal { get; set; }
    }
}
