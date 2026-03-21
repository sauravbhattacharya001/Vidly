using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// Severity level of damage found on a returned rental.
    /// </summary>
    public enum DamageSeverity
    {
        /// <summary>Minor cosmetic damage (light scratches, smudges).</summary>
        Minor,
        /// <summary>Moderate damage affecting disc readability or case integrity.</summary>
        Moderate,
        /// <summary>Severe damage rendering the item unplayable or unsellable.</summary>
        Severe,
        /// <summary>Item is completely destroyed or missing.</summary>
        Destroyed
    }

    /// <summary>
    /// Current resolution status of a damage report.
    /// </summary>
    public enum DamageStatus
    {
        /// <summary>Report filed, awaiting review.</summary>
        Open,
        /// <summary>Staff has reviewed and assessed the fee.</summary>
        Assessed,
        /// <summary>Customer has paid the damage fee.</summary>
        Paid,
        /// <summary>Fee was waived (goodwill, first offense, etc.).</summary>
        Waived,
        /// <summary>Customer is disputing the damage charge.</summary>
        Disputed
    }

    /// <summary>
    /// Type of damage observed on the returned item.
    /// </summary>
    public enum DamageType
    {
        ScratchedDisc,
        CrackedCase,
        MissingDisc,
        WaterDamage,
        BrokenHinge,
        TornInsert,
        WrittenOn,
        Stained,
        Other
    }

    /// <summary>
    /// A single damage report logged when a rental is returned in poor condition.
    /// Links to the customer, movie, and optional rental for traceability.
    /// </summary>
    public class DamageReport
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int MovieId { get; set; }
        public string MovieTitle { get; set; }
        public int? RentalId { get; set; }
        public DamageType DamageType { get; set; }
        public DamageSeverity Severity { get; set; }
        public DamageStatus Status { get; set; }
        public string Description { get; set; }
        public decimal AssessedFee { get; set; }
        public string StaffNotes { get; set; }
        public DateTime ReportedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
    }

    /// <summary>
    /// Aggregate statistics for the damage dashboard.
    /// </summary>
    public class DamageSummary
    {
        public int TotalReports { get; set; }
        public int OpenReports { get; set; }
        public int ResolvedReports { get; set; }
        public decimal TotalFeesAssessed { get; set; }
        public decimal TotalFeesCollected { get; set; }
        public decimal TotalFeesWaived { get; set; }
        public Dictionary<DamageSeverity, int> BySeverity { get; set; }
        public Dictionary<DamageType, int> ByType { get; set; }
    }
}
