using System;
using System.Collections.Generic;
using System.Linq;

namespace Vidly.Models
{
    // ───────────────────────── Enums ─────────────────────────

    /// <summary>Shelf zone in the store.</summary>
    public enum ShelfZone
    {
        NewReleases,
        Action,
        Comedy,
        Drama,
        Horror,
        SciFi,
        Family,
        Documentary,
        Classics,
        StaffPicks,
        ReturnBin,
        BackRoom
    }

    /// <summary>Result of comparing physical count to system records.</summary>
    public enum DiscrepancyType
    {
        /// <summary>Physical count matches system.</summary>
        Match,
        /// <summary>Physical count is less than expected (missing discs).</summary>
        Shortage,
        /// <summary>Physical count exceeds expected (extra/unregistered discs).</summary>
        Surplus,
        /// <summary>Title was not scanned at all during the audit.</summary>
        NotScanned
    }

    /// <summary>Overall audit status.</summary>
    public enum AuditStatus
    {
        InProgress,
        Completed,
        Cancelled
    }

    /// <summary>Severity level for discrepancy.</summary>
    public enum DiscrepancySeverity
    {
        None,
        Low,
        Medium,
        High,
        Critical
    }

    // ───────────────────────── Models ─────────────────────────

    /// <summary>
    /// Shelf location assignment for a movie title.
    /// </summary>
    public class ShelfLocation
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public ShelfZone Zone { get; set; }
        public string Shelf { get; set; }
        public string Slot { get; set; }

        /// <summary>Full location string, e.g. "NewReleases / A3 / 12".</summary>
        public string FullLocation =>
            $"{Zone} / {Shelf ?? "?"} / {Slot ?? "?"}";

        public DateTime AssignedDate { get; set; }
    }

    /// <summary>
    /// A single scan entry during an audit — one movie counted.
    /// </summary>
    public class AuditScanEntry
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public int PhysicalCount { get; set; }
        public ShelfZone ScannedZone { get; set; }
        public DateTime ScannedAt { get; set; }
        public string ScannedBy { get; set; }
        public string Notes { get; set; }
    }

    /// <summary>
    /// Discrepancy result for a single title after audit completion.
    /// </summary>
    public class AuditDiscrepancy
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public int ExpectedOnShelf { get; set; }
        public int PhysicalCount { get; set; }
        public int ActiveRentals { get; set; }
        public int SystemTotal { get; set; }
        public DiscrepancyType Type { get; set; }
        public int Variance => PhysicalCount - ExpectedOnShelf;
        public DiscrepancySeverity Severity { get; set; }
        public ShelfZone? ExpectedZone { get; set; }
        public ShelfZone? ActualZone { get; set; }
        public bool IsMislocated => ExpectedZone.HasValue && ActualZone.HasValue
                                    && ExpectedZone != ActualZone;
    }

    /// <summary>
    /// A completed (or in-progress) inventory audit session.
    /// </summary>
    public class InventoryAudit
    {
        public int Id { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string AuditorName { get; set; }
        public AuditStatus Status { get; set; }
        public List<AuditScanEntry> Scans { get; set; } = new List<AuditScanEntry>();
        public List<AuditDiscrepancy> Discrepancies { get; set; } = new List<AuditDiscrepancy>();
        public List<ShelfZone> ZonesAudited { get; set; } = new List<ShelfZone>();

        /// <summary>Duration so far or total.</summary>
        public TimeSpan Duration => (CompletedAt ?? DateTime.Now) - StartedAt;

        /// <summary>Unique titles scanned.</summary>
        public int TitlesScanned => Scans.Select(s => s.MovieId).Distinct().Count();

        /// <summary>Total physical copies counted.</summary>
        public int TotalCounted => Scans.Sum(s => s.PhysicalCount);
    }

    /// <summary>
    /// High-level audit report with shrinkage analysis.
    /// </summary>
    public class AuditReport
    {
        public int AuditId { get; set; }
        public DateTime AuditDate { get; set; }
        public string AuditorName { get; set; }
        public TimeSpan Duration { get; set; }

        // Counts
        public int TotalTitles { get; set; }
        public int TitlesScanned { get; set; }
        public int TitlesNotScanned { get; set; }
        public int TotalSystemCopies { get; set; }
        public int TotalPhysicalCopies { get; set; }
        public int TotalActiveRentals { get; set; }

        // Discrepancy summary
        public int MatchCount { get; set; }
        public int ShortageCount { get; set; }
        public int SurplusCount { get; set; }
        public int MislocatedCount { get; set; }

        // Shrinkage
        public int ShrinkageUnits { get; set; }
        public double ShrinkageRate { get; set; }
        public decimal EstimatedShrinkageCost { get; set; }

        // Accuracy
        public double AccuracyRate { get; set; }

        // Top issues
        public List<AuditDiscrepancy> TopShortages { get; set; } = new List<AuditDiscrepancy>();
        public List<AuditDiscrepancy> MislocatedItems { get; set; } = new List<AuditDiscrepancy>();

        // Zone breakdown
        public List<ZoneAuditSummary> ZoneBreakdown { get; set; } = new List<ZoneAuditSummary>();

        // Trend
        public List<ShrinkageTrend> ShrinkageTrends { get; set; } = new List<ShrinkageTrend>();
    }

    /// <summary>Summary per zone.</summary>
    public class ZoneAuditSummary
    {
        public ShelfZone Zone { get; set; }
        public int TitlesExpected { get; set; }
        public int TitlesScanned { get; set; }
        public int ExpectedCopies { get; set; }
        public int PhysicalCopies { get; set; }
        public int Variance => PhysicalCopies - ExpectedCopies;
        public double AccuracyRate => ExpectedCopies > 0
            ? Math.Max(0, 1.0 - (double)Math.Abs(Variance) / ExpectedCopies)
            : 1.0;
    }

    /// <summary>Shrinkage trend data point.</summary>
    public class ShrinkageTrend
    {
        public int AuditId { get; set; }
        public DateTime Date { get; set; }
        public double ShrinkageRate { get; set; }
        public int ShrinkageUnits { get; set; }
    }
}
