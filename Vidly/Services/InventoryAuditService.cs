using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Physical inventory auditing — shelf location tracking, scan sessions,
    /// discrepancy detection, shrinkage analysis, and audit history.
    ///
    /// <para>Usage flow:
    /// 1. Assign shelf locations with <see cref="AssignShelfLocation"/>.
    /// 2. Start an audit with <see cref="StartAudit"/>.
    /// 3. Record physical counts with <see cref="RecordScan"/>.
    /// 4. Complete with <see cref="CompleteAudit"/> to detect discrepancies.
    /// 5. Generate report with <see cref="GenerateReport"/>.
    /// </para>
    /// </summary>
    public class InventoryAuditService
    {
        /// <summary>Default replacement cost per missing disc.</summary>
        public const decimal DefaultDiscCost = 14.99m;

        private readonly IMovieRepository _movieRepository;
        private readonly IRentalRepository _rentalRepository;
        private readonly InventoryService _inventoryService;

        private readonly Dictionary<int, ShelfLocation> _shelfLocations = new Dictionary<int, ShelfLocation>();
        private readonly List<InventoryAudit> _audits = new List<InventoryAudit>();
        private int _nextAuditId = 1;

        public InventoryAuditService(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository,
            InventoryService inventoryService)
        {
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
            _inventoryService = inventoryService
                ?? throw new ArgumentNullException(nameof(inventoryService));
        }

        // ───────────────── Shelf Locations ─────────────────

        /// <summary>Assign or update shelf location for a movie.</summary>
        public ShelfLocation AssignShelfLocation(int movieId, ShelfZone zone,
            string shelf = null, string slot = null)
        {
            var movie = _movieRepository.GetById(movieId);
            if (movie == null)
                throw new ArgumentException($"Movie {movieId} not found.");

            var loc = new ShelfLocation
            {
                MovieId = movieId,
                MovieName = movie.Name,
                Zone = zone,
                Shelf = shelf,
                Slot = slot,
                AssignedDate = DateTime.Now
            };
            _shelfLocations[movieId] = loc;
            return loc;
        }

        /// <summary>Get shelf location for a movie (null if unassigned).</summary>
        public ShelfLocation GetShelfLocation(int movieId)
        {
            return _shelfLocations.TryGetValue(movieId, out var loc) ? loc : null;
        }

        /// <summary>Get all assigned shelf locations.</summary>
        public List<ShelfLocation> GetAllShelfLocations()
        {
            return _shelfLocations.Values
                .OrderBy(l => l.Zone)
                .ThenBy(l => l.Shelf)
                .ThenBy(l => l.Slot)
                .ToList();
        }

        /// <summary>Get shelf locations for a specific zone.</summary>
        public List<ShelfLocation> GetLocationsByZone(ShelfZone zone)
        {
            return _shelfLocations.Values
                .Where(l => l.Zone == zone)
                .OrderBy(l => l.Shelf)
                .ThenBy(l => l.Slot)
                .ToList();
        }

        /// <summary>Remove shelf location assignment.</summary>
        public bool RemoveShelfLocation(int movieId)
        {
            return _shelfLocations.Remove(movieId);
        }

        // ───────────────── Audit Lifecycle ─────────────────

        /// <summary>Start a new inventory audit session.</summary>
        public InventoryAudit StartAudit(string auditorName,
            List<ShelfZone> zones = null)
        {
            if (string.IsNullOrWhiteSpace(auditorName))
                throw new ArgumentException("Auditor name is required.");

            // Don't allow concurrent audits
            var active = _audits.FirstOrDefault(a => a.Status == AuditStatus.InProgress);
            if (active != null)
                throw new InvalidOperationException(
                    $"Audit #{active.Id} is already in progress. Complete or cancel it first.");

            var audit = new InventoryAudit
            {
                Id = _nextAuditId++,
                StartedAt = DateTime.Now,
                AuditorName = auditorName,
                Status = AuditStatus.InProgress,
                ZonesAudited = zones ?? Enum.GetValues(typeof(ShelfZone))
                    .Cast<ShelfZone>().ToList()
            };
            _audits.Add(audit);
            return audit;
        }

        /// <summary>Get the currently active audit (null if none).</summary>
        public InventoryAudit GetActiveAudit()
        {
            return _audits.FirstOrDefault(a => a.Status == AuditStatus.InProgress);
        }

        /// <summary>Get an audit by ID.</summary>
        public InventoryAudit GetAudit(int auditId)
        {
            return _audits.FirstOrDefault(a => a.Id == auditId);
        }

        /// <summary>Get all audit history.</summary>
        public List<InventoryAudit> GetAuditHistory()
        {
            return _audits.OrderByDescending(a => a.StartedAt).ToList();
        }

        /// <summary>Record a physical scan/count for a movie during an audit.</summary>
        public AuditScanEntry RecordScan(int auditId, int movieId,
            int physicalCount, ShelfZone zone, string scannedBy = null,
            string notes = null)
        {
            var audit = _audits.FirstOrDefault(a => a.Id == auditId);
            if (audit == null)
                throw new ArgumentException($"Audit #{auditId} not found.");
            if (audit.Status != AuditStatus.InProgress)
                throw new InvalidOperationException(
                    $"Audit #{auditId} is not in progress.");
            if (physicalCount < 0)
                throw new ArgumentOutOfRangeException(nameof(physicalCount),
                    "Physical count cannot be negative.");

            var movie = _movieRepository.GetById(movieId);
            if (movie == null)
                throw new ArgumentException($"Movie {movieId} not found.");

            // Remove previous scan for same movie if exists (re-count)
            audit.Scans.RemoveAll(s => s.MovieId == movieId);

            var entry = new AuditScanEntry
            {
                MovieId = movieId,
                MovieName = movie.Name,
                PhysicalCount = physicalCount,
                ScannedZone = zone,
                ScannedAt = DateTime.Now,
                ScannedBy = scannedBy ?? audit.AuditorName,
                Notes = notes
            };
            audit.Scans.Add(entry);
            return entry;
        }

        /// <summary>
        /// Complete the audit — runs discrepancy detection and finalizes.
        /// </summary>
        public InventoryAudit CompleteAudit(int auditId)
        {
            var audit = _audits.FirstOrDefault(a => a.Id == auditId);
            if (audit == null)
                throw new ArgumentException($"Audit #{auditId} not found.");
            if (audit.Status != AuditStatus.InProgress)
                throw new InvalidOperationException(
                    $"Audit #{auditId} is not in progress.");

            audit.Discrepancies = DetectDiscrepancies(audit);
            audit.CompletedAt = DateTime.Now;
            audit.Status = AuditStatus.Completed;
            return audit;
        }

        /// <summary>Cancel an in-progress audit.</summary>
        public InventoryAudit CancelAudit(int auditId)
        {
            var audit = _audits.FirstOrDefault(a => a.Id == auditId);
            if (audit == null)
                throw new ArgumentException($"Audit #{auditId} not found.");
            if (audit.Status != AuditStatus.InProgress)
                throw new InvalidOperationException(
                    $"Audit #{auditId} is not in progress.");

            audit.CompletedAt = DateTime.Now;
            audit.Status = AuditStatus.Cancelled;
            return audit;
        }

        // ───────────────── Discrepancy Detection ─────────────────

        private List<AuditDiscrepancy> DetectDiscrepancies(InventoryAudit audit)
        {
            var movies = _movieRepository.GetAll();
            var rentals = _rentalRepository.GetAll();
            var scanLookup = audit.Scans.ToDictionary(s => s.MovieId);
            var results = new List<AuditDiscrepancy>();

            foreach (var movie in movies)
            {
                var systemTotal = _inventoryService.GetStockCount(movie.Id);
                var activeRentals = rentals.Count(r =>
                    r.MovieId == movie.Id && r.Status != RentalStatus.Returned);
                var expectedOnShelf = Math.Max(0, systemTotal - activeRentals);

                ShelfZone? expectedZone = _shelfLocations.TryGetValue(movie.Id, out var loc)
                    ? loc.Zone : (ShelfZone?)null;

                if (scanLookup.TryGetValue(movie.Id, out var scan))
                {
                    var type = scan.PhysicalCount == expectedOnShelf
                        ? DiscrepancyType.Match
                        : scan.PhysicalCount < expectedOnShelf
                            ? DiscrepancyType.Shortage
                            : DiscrepancyType.Surplus;

                    results.Add(new AuditDiscrepancy
                    {
                        MovieId = movie.Id,
                        MovieName = movie.Name,
                        ExpectedOnShelf = expectedOnShelf,
                        PhysicalCount = scan.PhysicalCount,
                        ActiveRentals = activeRentals,
                        SystemTotal = systemTotal,
                        Type = type,
                        Severity = ClassifySeverity(type, expectedOnShelf, scan.PhysicalCount),
                        ExpectedZone = expectedZone,
                        ActualZone = scan.ScannedZone
                    });
                }
                else
                {
                    // Title not scanned — only flag if it was in an audited zone
                    if (expectedZone.HasValue && audit.ZonesAudited.Contains(expectedZone.Value))
                    {
                        results.Add(new AuditDiscrepancy
                        {
                            MovieId = movie.Id,
                            MovieName = movie.Name,
                            ExpectedOnShelf = expectedOnShelf,
                            PhysicalCount = 0,
                            ActiveRentals = activeRentals,
                            SystemTotal = systemTotal,
                            Type = DiscrepancyType.NotScanned,
                            Severity = expectedOnShelf > 0
                                ? DiscrepancySeverity.Medium
                                : DiscrepancySeverity.Low,
                            ExpectedZone = expectedZone,
                            ActualZone = null
                        });
                    }
                }
            }

            return results.OrderByDescending(d => d.Severity)
                .ThenBy(d => d.Type)
                .ToList();
        }

        private static DiscrepancySeverity ClassifySeverity(
            DiscrepancyType type, int expected, int actual)
        {
            if (type == DiscrepancyType.Match)
                return DiscrepancySeverity.None;

            var variance = Math.Abs(actual - expected);
            if (type == DiscrepancyType.Shortage)
            {
                if (actual == 0 && expected > 0) return DiscrepancySeverity.Critical;
                if (variance >= 3) return DiscrepancySeverity.High;
                if (variance >= 2) return DiscrepancySeverity.Medium;
                return DiscrepancySeverity.Low;
            }

            // Surplus
            if (variance >= 3) return DiscrepancySeverity.Medium;
            return DiscrepancySeverity.Low;
        }

        // ───────────────── Reporting ─────────────────

        /// <summary>Generate a full audit report for a completed audit.</summary>
        public AuditReport GenerateReport(int auditId)
        {
            var audit = _audits.FirstOrDefault(a => a.Id == auditId);
            if (audit == null)
                throw new ArgumentException($"Audit #{auditId} not found.");
            if (audit.Status != AuditStatus.Completed)
                throw new InvalidOperationException(
                    $"Audit #{auditId} is not completed.");

            var allMovies = _movieRepository.GetAll();
            var shortages = audit.Discrepancies
                .Where(d => d.Type == DiscrepancyType.Shortage)
                .ToList();
            var surpluses = audit.Discrepancies
                .Where(d => d.Type == DiscrepancyType.Surplus)
                .ToList();
            var matches = audit.Discrepancies
                .Where(d => d.Type == DiscrepancyType.Match)
                .ToList();
            var mislocated = audit.Discrepancies
                .Where(d => d.IsMislocated)
                .ToList();

            var shrinkageUnits = shortages.Sum(s => Math.Abs(s.Variance));
            var totalSystemCopies = audit.Discrepancies.Sum(d => d.ExpectedOnShelf);

            var report = new AuditReport
            {
                AuditId = audit.Id,
                AuditDate = audit.StartedAt,
                AuditorName = audit.AuditorName,
                Duration = audit.Duration,
                TotalTitles = allMovies.Count,
                TitlesScanned = audit.TitlesScanned,
                TitlesNotScanned = audit.Discrepancies
                    .Count(d => d.Type == DiscrepancyType.NotScanned),
                TotalSystemCopies = totalSystemCopies,
                TotalPhysicalCopies = audit.TotalCounted,
                TotalActiveRentals = audit.Discrepancies.Sum(d => d.ActiveRentals),
                MatchCount = matches.Count,
                ShortageCount = shortages.Count,
                SurplusCount = surpluses.Count,
                MislocatedCount = mislocated.Count,
                ShrinkageUnits = shrinkageUnits,
                ShrinkageRate = totalSystemCopies > 0
                    ? (double)shrinkageUnits / totalSystemCopies
                    : 0.0,
                EstimatedShrinkageCost = shrinkageUnits * DefaultDiscCost,
                AccuracyRate = audit.Discrepancies.Count > 0
                    ? (double)matches.Count / audit.Discrepancies.Count(d =>
                        d.Type != DiscrepancyType.NotScanned)
                    : 1.0,
                TopShortages = shortages
                    .OrderByDescending(s => Math.Abs(s.Variance))
                    .Take(10)
                    .ToList(),
                MislocatedItems = mislocated,
                ZoneBreakdown = BuildZoneBreakdown(audit),
                ShrinkageTrends = BuildShrinkageTrends()
            };

            return report;
        }

        private List<ZoneAuditSummary> BuildZoneBreakdown(InventoryAudit audit)
        {
            var scansByZone = audit.Scans
                .GroupBy(s => s.ScannedZone)
                .Select(g => new ZoneAuditSummary
                {
                    Zone = g.Key,
                    TitlesScanned = g.Select(s => s.MovieId).Distinct().Count(),
                    PhysicalCopies = g.Sum(s => s.PhysicalCount),
                    TitlesExpected = _shelfLocations.Values
                        .Count(l => l.Zone == g.Key),
                    ExpectedCopies = _shelfLocations.Values
                        .Where(l => l.Zone == g.Key)
                        .Sum(l => Math.Max(0,
                            _inventoryService.GetStockCount(l.MovieId)
                            - CountActiveRentals(l.MovieId)))
                })
                .OrderBy(z => z.Zone)
                .ToList();

            return scansByZone;
        }

        private List<ShrinkageTrend> BuildShrinkageTrends()
        {
            return _audits
                .Where(a => a.Status == AuditStatus.Completed)
                .OrderBy(a => a.StartedAt)
                .Select(a =>
                {
                    var shortages = a.Discrepancies
                        .Where(d => d.Type == DiscrepancyType.Shortage)
                        .Sum(d => Math.Abs(d.Variance));
                    var total = a.Discrepancies.Sum(d => d.ExpectedOnShelf);
                    return new ShrinkageTrend
                    {
                        AuditId = a.Id,
                        Date = a.StartedAt,
                        ShrinkageUnits = shortages,
                        ShrinkageRate = total > 0 ? (double)shortages / total : 0.0
                    };
                })
                .ToList();
        }

        private int CountActiveRentals(int movieId)
        {
            return _rentalRepository.GetAll()
                .Count(r => r.MovieId == movieId && r.Status != RentalStatus.Returned);
        }

        // ───────────────── Quick Checks ─────────────────

        /// <summary>Find movies with no shelf location assigned.</summary>
        public List<Movie> GetUnlocatedMovies()
        {
            return _movieRepository.GetAll()
                .Where(m => !_shelfLocations.ContainsKey(m.Id))
                .ToList();
        }

        /// <summary>Get movies that haven't been audited recently.</summary>
        public List<Movie> GetStaleAuditMovies(int daysSinceLastAudit = 30)
        {
            var cutoff = DateTime.Now.AddDays(-daysSinceLastAudit);
            var recentlyAudited = new HashSet<int>();

            foreach (var audit in _audits.Where(a =>
                a.Status == AuditStatus.Completed && a.StartedAt >= cutoff))
            {
                foreach (var scan in audit.Scans)
                    recentlyAudited.Add(scan.MovieId);
            }

            return _movieRepository.GetAll()
                .Where(m => !recentlyAudited.Contains(m.Id))
                .ToList();
        }

        /// <summary>
        /// Quick spot-check: compare a single movie's physical count against
        /// system records without running a full audit.
        /// </summary>
        public AuditDiscrepancy SpotCheck(int movieId, int physicalCount,
            ShelfZone zone)
        {
            var movie = _movieRepository.GetById(movieId);
            if (movie == null)
                throw new ArgumentException($"Movie {movieId} not found.");
            if (physicalCount < 0)
                throw new ArgumentOutOfRangeException(nameof(physicalCount));

            var systemTotal = _inventoryService.GetStockCount(movieId);
            var activeRentals = CountActiveRentals(movieId);
            var expectedOnShelf = Math.Max(0, systemTotal - activeRentals);

            ShelfZone? expectedZone = _shelfLocations.TryGetValue(movieId, out var loc)
                ? loc.Zone : (ShelfZone?)null;

            var type = physicalCount == expectedOnShelf
                ? DiscrepancyType.Match
                : physicalCount < expectedOnShelf
                    ? DiscrepancyType.Shortage
                    : DiscrepancyType.Surplus;

            return new AuditDiscrepancy
            {
                MovieId = movieId,
                MovieName = movie.Name,
                ExpectedOnShelf = expectedOnShelf,
                PhysicalCount = physicalCount,
                ActiveRentals = activeRentals,
                SystemTotal = systemTotal,
                Type = type,
                Severity = ClassifySeverity(type, expectedOnShelf, physicalCount),
                ExpectedZone = expectedZone,
                ActualZone = zone
            };
        }
    }
}
