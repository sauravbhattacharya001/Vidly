using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Vidly.Models
{
    /// <summary>
    /// Represents a staff member at the rental store.
    /// </summary>
    public class StaffMember
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Staff name is required.")]
        [StringLength(255)]
        public string Name { get; set; }

        [EmailAddress]
        [StringLength(255)]
        public string Email { get; set; }

        [Display(Name = "Hire Date")]
        [DataType(DataType.Date)]
        public DateTime HireDate { get; set; }

        [Display(Name = "Role")]
        public StaffRole Role { get; set; }

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Tenure in days from hire date.
        /// </summary>
        public int TenureDays => Math.Max(0, (int)(DateTime.Today - HireDate).TotalDays);
    }

    /// <summary>
    /// Staff role within the store.
    /// </summary>
    public enum StaffRole
    {
        [Display(Name = "Clerk")]
        Clerk = 1,

        [Display(Name = "Senior Clerk")]
        SeniorClerk = 2,

        [Display(Name = "Shift Lead")]
        ShiftLead = 3,

        [Display(Name = "Manager")]
        Manager = 4
    }

    /// <summary>
    /// A transaction processed by a staff member.
    /// </summary>
    public class StaffTransaction
    {
        public int Id { get; set; }

        [Required]
        public int StaffId { get; set; }

        public string StaffName { get; set; }

        [Required]
        public int CustomerId { get; set; }

        public string CustomerName { get; set; }

        public int? MovieId { get; set; }
        public string MovieName { get; set; }

        [Required]
        public StaffTransactionType Type { get; set; }

        [DataType(DataType.DateTime)]
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Revenue associated with this transaction.
        /// </summary>
        [Range(0, double.MaxValue)]
        public decimal Revenue { get; set; }

        /// <summary>
        /// Duration of the transaction in seconds.
        /// </summary>
        [Range(0, int.MaxValue)]
        public int DurationSeconds { get; set; }

        /// <summary>
        /// Whether the staff member suggested an upsell (e.g., bundle, longer rental, premium).
        /// </summary>
        public bool UpsellAttempted { get; set; }

        /// <summary>
        /// Whether the customer accepted the upsell.
        /// </summary>
        public bool UpsellAccepted { get; set; }

        /// <summary>
        /// Optional customer satisfaction rating (1-5) collected at point of sale.
        /// </summary>
        [Range(1, 5)]
        public int? SatisfactionRating { get; set; }

        /// <summary>
        /// Optional feedback comment from the customer.
        /// </summary>
        [StringLength(500)]
        public string FeedbackComment { get; set; }
    }

    /// <summary>
    /// Type of transaction processed by staff.
    /// </summary>
    public enum StaffTransactionType
    {
        Rental = 1,
        Return = 2,
        Reservation = 3,
        GiftCardSale = 4,
        MembershipUpgrade = 5
    }

    /// <summary>
    /// Performance metrics for a single staff member over a time period.
    /// </summary>
    public class StaffPerformanceReport
    {
        public int StaffId { get; set; }
        public string StaffName { get; set; }
        public StaffRole Role { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        // Volume
        public int TotalTransactions { get; set; }
        public int RentalCount { get; set; }
        public int ReturnCount { get; set; }
        public int ReservationCount { get; set; }
        public int GiftCardSaleCount { get; set; }
        public int MembershipUpgradeCount { get; set; }

        // Revenue
        public decimal TotalRevenue { get; set; }
        public decimal AverageRevenuePerTransaction { get; set; }

        // Speed
        public double AverageTransactionSeconds { get; set; }
        public int FastestTransactionSeconds { get; set; }
        public int SlowestTransactionSeconds { get; set; }

        // Upsell
        public int UpsellAttempts { get; set; }
        public int UpsellSuccesses { get; set; }
        public double UpsellConversionRate { get; set; }

        // Satisfaction
        public double AverageSatisfactionRating { get; set; }
        public int TotalRatings { get; set; }
        public int FiveStarCount { get; set; }
        public int OneStarCount { get; set; }

        // Composite
        public double PerformanceScore { get; set; }
        public string Grade { get; set; }
        public List<string> Strengths { get; set; } = new List<string>();
        public List<string> ImprovementAreas { get; set; } = new List<string>();
    }

    /// <summary>
    /// Ranking entry for staff leaderboard.
    /// </summary>
    public class StaffRankingEntry
    {
        public int Rank { get; set; }
        public int StaffId { get; set; }
        public string StaffName { get; set; }
        public StaffRole Role { get; set; }
        public double Score { get; set; }
        public string Grade { get; set; }
        public int Transactions { get; set; }
        public decimal Revenue { get; set; }
        public double SatisfactionAvg { get; set; }
    }

    /// <summary>
    /// Team-level performance summary.
    /// </summary>
    public class TeamPerformanceSummary
    {
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public int ActiveStaffCount { get; set; }
        public int TotalTransactions { get; set; }
        public decimal TotalRevenue { get; set; }
        public double AvgTransactionsPerStaff { get; set; }
        public double AvgRevenuePerStaff { get; set; }
        public double TeamSatisfactionAvg { get; set; }
        public double TeamUpsellRate { get; set; }
        public double AvgTransactionSeconds { get; set; }
        public StaffRankingEntry TopPerformer { get; set; }
        public StaffRankingEntry MostImproved { get; set; }
        public List<StaffRankingEntry> Rankings { get; set; } = new List<StaffRankingEntry>();
        public Dictionary<StaffTransactionType, int> TransactionBreakdown { get; set; } = new Dictionary<StaffTransactionType, int>();

        /// <summary>
        /// Generate a text summary of team performance.
        /// </summary>
        public string ToTextReport()
        {
            var lines = new List<string>
            {
                "═══ TEAM PERFORMANCE REPORT ═══",
                $"Period: {PeriodStart:yyyy-MM-dd} to {PeriodEnd:yyyy-MM-dd}",
                $"Active Staff: {ActiveStaffCount}",
                "",
                "── Overview ──",
                $"Total Transactions: {TotalTransactions:N0}",
                $"Total Revenue: ${TotalRevenue:N2}",
                $"Avg Transactions/Staff: {AvgTransactionsPerStaff:F1}",
                $"Avg Revenue/Staff: ${AvgRevenuePerStaff:N2}",
                $"Team Satisfaction: {TeamSatisfactionAvg:F2}/5.0",
                $"Team Upsell Rate: {TeamUpsellRate:P1}",
                $"Avg Transaction Time: {AvgTransactionSeconds:F0}s",
            };

            if (TransactionBreakdown.Count > 0)
            {
                lines.Add("");
                lines.Add("── Transaction Breakdown ──");
                foreach (var kv in TransactionBreakdown.OrderByDescending(x => x.Value))
                    lines.Add($"  {kv.Key}: {kv.Value}");
            }

            if (TopPerformer != null)
            {
                lines.Add("");
                lines.Add($"🏆 Top Performer: {TopPerformer.StaffName} (Score: {TopPerformer.Score:F1}, Grade: {TopPerformer.Grade})");
            }

            if (Rankings.Count > 0)
            {
                lines.Add("");
                lines.Add("── Rankings ──");
                foreach (var r in Rankings)
                    lines.Add($"  #{r.Rank} {r.StaffName} — Score: {r.Score:F1} ({r.Grade}), {r.Transactions} txns, ${r.Revenue:N2}");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}

