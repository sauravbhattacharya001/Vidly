using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// Severity level for a detected revenue leak.
    /// </summary>
    public enum LeakSeverity
    {
        [Display(Name = "Low")]
        Low = 1,

        [Display(Name = "Medium")]
        Medium = 2,

        [Display(Name = "High")]
        High = 3,

        [Display(Name = "Critical")]
        Critical = 4
    }

    /// <summary>
    /// Category of revenue leak source.
    /// </summary>
    public enum LeakCategory
    {
        [Display(Name = "Uncollected Late Fees")]
        UncollectedLateFees,

        [Display(Name = "Expired Gift Cards")]
        ExpiredGiftCards,

        [Display(Name = "Underpriced Titles")]
        UnderpricedTitles,

        [Display(Name = "Idle Inventory")]
        IdleInventory,

        [Display(Name = "Lapsed Subscribers")]
        LapsedSubscribers,

        [Display(Name = "Underutilized Subscriptions")]
        UnderutilizedSubscriptions,

        [Display(Name = "Overdue Unreturned")]
        OverdueUnreturned,

        [Display(Name = "Dormant Customers")]
        DormantCustomers
    }

    /// <summary>
    /// A single detected revenue leak with estimated dollar impact.
    /// </summary>
    public class RevenueLeak
    {
        /// <summary>Source category of this leak.</summary>
        public LeakCategory Category { get; set; }

        /// <summary>Severity classification.</summary>
        public LeakSeverity Severity { get; set; }

        /// <summary>Human-readable description of the leak.</summary>
        public string Description { get; set; }

        /// <summary>Estimated recoverable revenue in dollars.</summary>
        [DataType(DataType.Currency)]
        public decimal EstimatedImpact { get; set; }

        /// <summary>Number of affected entities (customers, rentals, cards, etc.).</summary>
        public int AffectedCount { get; set; }

        /// <summary>Specific remediation action to take.</summary>
        public string Remediation { get; set; }

        /// <summary>Confidence in the estimate (0.0 to 1.0).</summary>
        public double Confidence { get; set; }

        /// <summary>Affected entity IDs for drill-down.</summary>
        public List<int> AffectedEntityIds { get; set; }

        public RevenueLeak()
        {
            AffectedEntityIds = new List<int>();
            Confidence = 0.5;
        }
    }

    /// <summary>
    /// A recommended remediation action with priority and effort estimate.
    /// </summary>
    public class RemediationAction
    {
        /// <summary>Action title.</summary>
        public string Title { get; set; }

        /// <summary>Detailed description of what to do.</summary>
        public string Description { get; set; }

        /// <summary>Priority rank (1 = highest).</summary>
        public int Priority { get; set; }

        /// <summary>Estimated revenue recoverable if action is taken.</summary>
        [DataType(DataType.Currency)]
        public decimal PotentialRecovery { get; set; }

        /// <summary>Effort level: Low, Medium, High.</summary>
        public string Effort { get; set; }

        /// <summary>Related leak categories this action addresses.</summary>
        public List<LeakCategory> RelatedCategories { get; set; }

        public RemediationAction()
        {
            RelatedCategories = new List<LeakCategory>();
            Effort = "Medium";
        }
    }

    /// <summary>
    /// Full revenue leakage analysis report.
    /// </summary>
    public class RevenueLeakageReport
    {
        /// <summary>When this report was generated.</summary>
        public DateTime GeneratedAt { get; set; }

        /// <summary>Overall revenue health score (0-100, higher = less leakage).</summary>
        public int HealthScore { get; set; }

        /// <summary>Total estimated revenue leakage across all categories.</summary>
        [DataType(DataType.Currency)]
        public decimal TotalLeakage { get; set; }

        /// <summary>Total potentially recoverable revenue.</summary>
        [DataType(DataType.Currency)]
        public decimal RecoverableRevenue { get; set; }

        /// <summary>All detected leaks, ordered by impact descending.</summary>
        public List<RevenueLeak> Leaks { get; set; }

        /// <summary>Prioritized remediation playbook.</summary>
        public List<RemediationAction> Playbook { get; set; }

        /// <summary>Breakdown of leakage by category.</summary>
        public Dictionary<LeakCategory, decimal> CategoryBreakdown { get; set; }

        /// <summary>Trend indicator: "improving", "stable", or "worsening" vs previous period.</summary>
        public string Trend { get; set; }

        /// <summary>Number of leak detectors that ran.</summary>
        public int DetectorsRun { get; set; }

        public RevenueLeakageReport()
        {
            Leaks = new List<RevenueLeak>();
            Playbook = new List<RemediationAction>();
            CategoryBreakdown = new Dictionary<LeakCategory, decimal>();
            Trend = "stable";
        }
    }
}
