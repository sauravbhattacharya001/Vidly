using System;
using System.Collections.Generic;
using System.Linq;

namespace Vidly.Models
{
    /// <summary>
    /// Configuration for membership tier thresholds and benefits.
    /// </summary>
    public class TierConfig
    {
        /// <summary>Minimum rentals in evaluation period to qualify.</summary>
        public int MinRentals { get; set; }

        /// <summary>Minimum total spend in evaluation period to qualify.</summary>
        public decimal MinSpend { get; set; }

        /// <summary>Maximum allowed late return percentage (0.0–1.0).</summary>
        public double MaxLatePercentage { get; set; }

        /// <summary>Discount percentage on daily rate (0–100).</summary>
        public int DiscountPercent { get; set; }

        /// <summary>Maximum concurrent active rentals allowed.</summary>
        public int MaxConcurrentRentals { get; set; }

        /// <summary>Extra grace days before a rental is considered late.</summary>
        public int GraceDays { get; set; }

        /// <summary>Whether the member gets free reservations.</summary>
        public bool FreeReservations { get; set; }

        /// <summary>Whether the member gets priority new releases.</summary>
        public bool PriorityNewReleases { get; set; }
    }

    /// <summary>
    /// Benefits associated with a membership tier.
    /// </summary>
    public class TierBenefits
    {
        public MembershipType Tier { get; set; }
        public string TierName { get; set; }
        public int DiscountPercent { get; set; }
        public int MaxConcurrentRentals { get; set; }
        public int GraceDays { get; set; }
        public bool FreeReservations { get; set; }
        public bool PriorityNewReleases { get; set; }
    }

    /// <summary>
    /// Result of evaluating a customer's tier qualification.
    /// </summary>
    public class TierEvaluation
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public MembershipType CurrentTier { get; set; }
        public MembershipType EvaluatedTier { get; set; }
        public bool TierChanged => CurrentTier != EvaluatedTier;
        public bool IsUpgrade => (int)EvaluatedTier > (int)CurrentTier;
        public bool IsDowngrade => (int)EvaluatedTier < (int)CurrentTier;
        public int RentalsInPeriod { get; set; }
        public decimal SpendInPeriod { get; set; }
        public double LatePercentage { get; set; }
        public int OnTimeReturns { get; set; }
        public int LateReturns { get; set; }
        public TierBenefits CurrentBenefits { get; set; }
        public TierBenefits NewBenefits { get; set; }
        public string Reason { get; set; }

        /// <summary>Progress toward next tier (0.0–1.0), or 1.0 if at max.</summary>
        public double ProgressToNextTier { get; set; }

        /// <summary>What the customer needs to reach the next tier.</summary>
        public string NextTierRequirement { get; set; }
    }

    /// <summary>
    /// Record of a tier change event.
    /// </summary>
    public class TierChangeRecord
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public MembershipType OldTier { get; set; }
        public MembershipType NewTier { get; set; }
        public DateTime ChangeDate { get; set; }
        public string Reason { get; set; }
        public bool IsUpgrade => (int)NewTier > (int)OldTier;
    }

    /// <summary>
    /// Tier comparison showing benefits at each level.
    /// </summary>
    public class TierComparison
    {
        public List<TierBenefits> Tiers { get; set; } = new List<TierBenefits>();
    }

    /// <summary>
    /// Fleet-wide tier distribution analytics.
    /// </summary>
    public class TierDistribution
    {
        public int TotalCustomers { get; set; }
        public Dictionary<MembershipType, int> Counts { get; set; } = new Dictionary<MembershipType, int>();
        public Dictionary<MembershipType, double> Percentages { get; set; } = new Dictionary<MembershipType, double>();
        public MembershipType MostCommonTier { get; set; }
        public double AverageSpendByTier(MembershipType tier, List<TierEvaluation> evals)
        {
            var tierEvals = evals.Where(e => e.CurrentTier == tier).ToList();
            return tierEvals.Count == 0 ? 0 : (double)tierEvals.Average(e => e.SpendInPeriod);
        }
    }

    /// <summary>
    /// Full membership tier report for a customer.
    /// </summary>
    public class MembershipReport
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public MembershipType CurrentTier { get; set; }
        public TierBenefits Benefits { get; set; }
        public TierEvaluation LatestEvaluation { get; set; }
        public List<TierChangeRecord> History { get; set; } = new List<TierChangeRecord>();
        public int TotalRentals { get; set; }
        public decimal TotalSpend { get; set; }
        public double LifetimeLatePercentage { get; set; }
        public DateTime? MemberSince { get; set; }
        public int MembershipDays { get; set; }

        public string GenerateTextReport()
        {
            var lines = new List<string>
            {
                $"=== Membership Report: {CustomerName} ===",
                $"Member Since: {(MemberSince.HasValue ? MemberSince.Value.ToString("yyyy-MM-dd") : "N/A")}",
                $"Membership Duration: {MembershipDays} days",
                $"Current Tier: {CurrentTier}",
                "",
                "--- Benefits ---",
                $"  Discount: {Benefits.DiscountPercent}%",
                $"  Max Concurrent Rentals: {Benefits.MaxConcurrentRentals}",
                $"  Grace Days: {Benefits.GraceDays}",
                $"  Free Reservations: {(Benefits.FreeReservations ? "Yes" : "No")}",
                $"  Priority New Releases: {(Benefits.PriorityNewReleases ? "Yes" : "No")}",
                "",
                "--- Activity ---",
                $"  Total Rentals: {TotalRentals}",
                $"  Total Spend: ${TotalSpend:F2}",
                $"  Lifetime Late %: {LifetimeLatePercentage:P1}",
            };

            if (LatestEvaluation != null)
            {
                lines.Add("");
                lines.Add("--- Latest Evaluation ---");
                lines.Add($"  Rentals (period): {LatestEvaluation.RentalsInPeriod}");
                lines.Add($"  Spend (period): ${LatestEvaluation.SpendInPeriod:F2}");
                lines.Add($"  Late %: {LatestEvaluation.LatePercentage:P1}");
                lines.Add($"  Evaluated Tier: {LatestEvaluation.EvaluatedTier}");
                if (LatestEvaluation.NextTierRequirement != null)
                    lines.Add($"  Next Tier: {LatestEvaluation.NextTierRequirement}");
            }

            if (History.Count > 0)
            {
                lines.Add("");
                lines.Add($"--- Tier History ({History.Count} changes) ---");
                foreach (var h in History.OrderByDescending(x => x.ChangeDate))
                    lines.Add($"  {h.ChangeDate:yyyy-MM-dd}: {h.OldTier} → {h.NewTier} ({h.Reason})");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
