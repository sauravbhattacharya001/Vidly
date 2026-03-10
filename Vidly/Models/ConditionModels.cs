using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    // ── Enums ────────────────────────────────────────────────────────

    /// <summary>Physical condition grade for a movie copy.</summary>
    public enum ConditionGrade
    {
        /// <summary>Like new — no visible wear.</summary>
        Mint = 5,

        /// <summary>Minor surface marks, fully playable.</summary>
        Good = 4,

        /// <summary>Noticeable scratches, occasional playback issues.</summary>
        Fair = 3,

        /// <summary>Significant damage, frequent skipping.</summary>
        Poor = 2,

        /// <summary>Unplayable or missing — must replace.</summary>
        Damaged = 1
    }

    /// <summary>Type of condition inspection (checkout vs return).</summary>
    public enum InspectionType
    {
        Checkout = 1,
        Return = 2,
        Audit = 3
    }

    /// <summary>Risk level for a customer based on condition history.</summary>
    public enum RenterRiskLevel
    {
        Low = 1,
        Medium = 2,
        High = 3
    }

    // ── Core Models ─────────────────────────────────────────────────

    /// <summary>
    /// A single condition inspection record for a movie copy, taken at
    /// checkout or return. Tracks disc condition, case condition, and
    /// free-text notes from the inspector.
    /// </summary>
    public class ConditionInspection
    {
        public int Id { get; set; }
        public int MovieId { get; set; }
        public int RentalId { get; set; }
        public int CustomerId { get; set; }
        public InspectionType Type { get; set; }
        public ConditionGrade DiscGrade { get; set; }
        public ConditionGrade CaseGrade { get; set; }
        public string Notes { get; set; }
        public string InspectorName { get; set; }
        public DateTime InspectedAt { get; set; }
    }

    /// <summary>
    /// Pairs the checkout and return inspections for a single rental to
    /// show how much a copy deteriorated during one rental period.
    /// </summary>
    public class RentalConditionDelta
    {
        public int RentalId { get; set; }
        public int MovieId { get; set; }
        public int CustomerId { get; set; }
        public ConditionGrade DiscBefore { get; set; }
        public ConditionGrade DiscAfter { get; set; }
        public ConditionGrade CaseBefore { get; set; }
        public ConditionGrade CaseAfter { get; set; }

        /// <summary>
        /// Negative value means deterioration (e.g. Good→Fair = -1).
        /// </summary>
        public int DiscChange => (int)DiscAfter - (int)DiscBefore;

        /// <summary>
        /// Negative value means deterioration.
        /// </summary>
        public int CaseChange => (int)CaseAfter - (int)CaseBefore;

        /// <summary>
        /// True if either disc or case got worse.
        /// </summary>
        public bool Deteriorated => DiscChange < 0 || CaseChange < 0;
    }

    /// <summary>
    /// Current condition status of a specific movie copy, including
    /// deterioration trend over its lifetime.
    /// </summary>
    public class CopyConditionStatus
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public ConditionGrade CurrentDiscGrade { get; set; }
        public ConditionGrade CurrentCaseGrade { get; set; }
        public int TotalRentals { get; set; }
        public int DeteriorationEvents { get; set; }

        /// <summary>
        /// Average grade drop per rental (0 = no degradation, negative = worsening).
        /// </summary>
        public double DeteriorationRate { get; set; }

        public bool NeedsReplacement =>
            CurrentDiscGrade <= ConditionGrade.Poor ||
            CurrentCaseGrade <= ConditionGrade.Poor;

        public DateTime? LastInspection { get; set; }
    }

    /// <summary>
    /// Risk profile for a customer based on their condition history.
    /// </summary>
    public class RenterRiskProfile
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int TotalRentals { get; set; }
        public int DamageEvents { get; set; }
        public double DamageRate { get; set; }
        public double AverageDiscReturn { get; set; }
        public double AverageCaseReturn { get; set; }
        public RenterRiskLevel RiskLevel { get; set; }
    }

    /// <summary>
    /// Summary report of the entire inventory's condition.
    /// </summary>
    public class ConditionReport
    {
        public int TotalCopies { get; set; }
        public int MintCount { get; set; }
        public int GoodCount { get; set; }
        public int FairCount { get; set; }
        public int PoorCount { get; set; }
        public int DamagedCount { get; set; }
        public int NeedingReplacement { get; set; }
        public double AverageDiscGrade { get; set; }
        public double AverageCaseGrade { get; set; }
        public List<CopyConditionStatus> WorstCopies { get; set; } = new List<CopyConditionStatus>();
        public List<RenterRiskProfile> HighRiskRenters { get; set; } = new List<RenterRiskProfile>();
        public DateTime GeneratedAt { get; set; }
    }
}
