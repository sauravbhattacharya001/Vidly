using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class InventoryAuditServiceTests
    {
        private class TestMovieRepository : IMovieRepository
        {
            private readonly Dictionary<int, Movie> _movies = new Dictionary<int, Movie>();
            public void Add(Movie movie) => _movies[movie.Id] = movie;
            public Movie GetById(int id) => _movies.TryGetValue(id, out var m) ? m : null;
            public IReadOnlyList<Movie> GetAll() => _movies.Values.ToList().AsReadOnly();
            public void Update(Movie movie) { if (_movies.ContainsKey(movie.Id)) _movies[movie.Id] = movie; }
            public void Remove(int id) => _movies.Remove(id);
            public Movie GetRandom() => _movies.Values.FirstOrDefault();
            public IReadOnlyList<Movie> Search(string q, Genre? g, int? r) => GetAll();
            public IReadOnlyList<Movie> GetByReleaseDate(int y, int m) => GetAll();
        }

        private class TestRentalRepository : IRentalRepository
        {
            private readonly List<Rental> _rentals = new List<Rental>();
            private int _nextId = 1;
            public void Add(Rental r) { r.Id = _nextId++; _rentals.Add(r); }
            public Rental GetById(int id) => _rentals.FirstOrDefault(r => r.Id == id);
            public IReadOnlyList<Rental> GetAll() => _rentals.AsReadOnly();
            public void Update(Rental r) { var i = _rentals.FindIndex(x => x.Id == r.Id); if (i >= 0) _rentals[i] = r; }
            public void Remove(int id) => _rentals.RemoveAll(r => r.Id == id);
            public IReadOnlyList<Rental> GetActiveByCustomer(int customerId) =>
                _rentals.Where(r => r.CustomerId == customerId && r.Status != RentalStatus.Returned).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetByMovie(int movieId) =>
                _rentals.Where(r => r.MovieId == movieId).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetOverdue() =>
                _rentals.Where(r => r.IsOverdue).ToList().AsReadOnly();
            public IReadOnlyList<Rental> Search(string query, RentalStatus? status) => GetAll();
            public Rental ReturnRental(int rentalId) { var r = GetById(rentalId); if (r != null) r.Status = RentalStatus.Returned; return r; }
            public bool IsMovieRentedOut(int movieId) => _rentals.Any(r => r.MovieId == movieId && r.Status != RentalStatus.Returned);
            public Rental Checkout(Rental rental) { Add(rental); return rental; }
            public Rental Checkout(Rental rental, int maxConcurrentRentals) { Add(rental); return rental; }
            public RentalStats GetStats() => new RentalStats();
        }

        private TestMovieRepository _movieRepo;
        private TestRentalRepository _rentalRepo;
        private InventoryService _inventoryService;
        private InventoryAuditService _service;

        [TestInitialize]
        public void Setup()
        {
            _movieRepo = new TestMovieRepository();
            _rentalRepo = new TestRentalRepository();
            _inventoryService = new InventoryService(_rentalRepo, _movieRepo);
            _service = new InventoryAuditService(_movieRepo, _rentalRepo, _inventoryService);

            _movieRepo.Add(new Movie { Id = 1, Name = "The Matrix", Genre = Genre.Action });
            _movieRepo.Add(new Movie { Id = 2, Name = "Toy Story", Genre = Genre.Animation });
            _movieRepo.Add(new Movie { Id = 3, Name = "Alien", Genre = Genre.SciFi });
            _movieRepo.Add(new Movie { Id = 4, Name = "The Notebook", Genre = Genre.Romance });
        }

        // ─── Shelf Locations ───

        [TestMethod]
        public void AssignShelfLocation_ValidMovie_ReturnsLocation()
        {
            var loc = _service.AssignShelfLocation(1, ShelfZone.Action, "A", "3");
            Assert.AreEqual(1, loc.MovieId);
            Assert.AreEqual("The Matrix", loc.MovieName);
            Assert.AreEqual(ShelfZone.Action, loc.Zone);
            Assert.AreEqual("A", loc.Shelf);
            Assert.AreEqual("3", loc.Slot);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void AssignShelfLocation_InvalidMovie_Throws()
        {
            _service.AssignShelfLocation(999, ShelfZone.Action);
        }

        [TestMethod]
        public void GetShelfLocation_Assigned_ReturnsLocation()
        {
            _service.AssignShelfLocation(1, ShelfZone.Action);
            var loc = _service.GetShelfLocation(1);
            Assert.IsNotNull(loc);
            Assert.AreEqual(ShelfZone.Action, loc.Zone);
        }

        [TestMethod]
        public void GetShelfLocation_NotAssigned_ReturnsNull()
        {
            Assert.IsNull(_service.GetShelfLocation(1));
        }

        [TestMethod]
        public void GetAllShelfLocations_ReturnsOrdered()
        {
            _service.AssignShelfLocation(1, ShelfZone.Action, "B");
            _service.AssignShelfLocation(2, ShelfZone.Action, "A");
            _service.AssignShelfLocation(3, ShelfZone.SciFi);
            var all = _service.GetAllShelfLocations();
            Assert.AreEqual(3, all.Count);
            Assert.AreEqual(ShelfZone.Action, all[0].Zone);
            Assert.AreEqual("A", all[0].Shelf);
        }

        [TestMethod]
        public void GetLocationsByZone_FiltersCorrectly()
        {
            _service.AssignShelfLocation(1, ShelfZone.Action);
            _service.AssignShelfLocation(2, ShelfZone.Family);
            _service.AssignShelfLocation(3, ShelfZone.Action);
            var action = _service.GetLocationsByZone(ShelfZone.Action);
            Assert.AreEqual(2, action.Count);
        }

        [TestMethod]
        public void RemoveShelfLocation_Exists_ReturnsTrue()
        {
            _service.AssignShelfLocation(1, ShelfZone.Action);
            Assert.IsTrue(_service.RemoveShelfLocation(1));
            Assert.IsNull(_service.GetShelfLocation(1));
        }

        [TestMethod]
        public void RemoveShelfLocation_NotExists_ReturnsFalse()
        {
            Assert.IsFalse(_service.RemoveShelfLocation(1));
        }

        [TestMethod]
        public void FullLocation_FormatsCorrectly()
        {
            var loc = _service.AssignShelfLocation(1, ShelfZone.NewReleases, "B2", "7");
            Assert.AreEqual("NewReleases / B2 / 7", loc.FullLocation);
        }

        // ─── Audit Lifecycle ───

        [TestMethod]
        public void StartAudit_CreatesInProgressAudit()
        {
            var audit = _service.StartAudit("Alice");
            Assert.AreEqual(1, audit.Id);
            Assert.AreEqual("Alice", audit.AuditorName);
            Assert.AreEqual(AuditStatus.InProgress, audit.Status);
            Assert.IsNull(audit.CompletedAt);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void StartAudit_EmptyName_Throws()
        {
            _service.StartAudit("");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void StartAudit_ConcurrentAudit_Throws()
        {
            _service.StartAudit("Alice");
            _service.StartAudit("Bob");
        }

        [TestMethod]
        public void StartAudit_WithZones_ScopesAudit()
        {
            var zones = new List<ShelfZone> { ShelfZone.Action, ShelfZone.Comedy };
            var audit = _service.StartAudit("Alice", zones);
            Assert.AreEqual(2, audit.ZonesAudited.Count);
        }

        [TestMethod]
        public void GetActiveAudit_ReturnsActive()
        {
            Assert.IsNull(_service.GetActiveAudit());
            _service.StartAudit("Alice");
            Assert.IsNotNull(_service.GetActiveAudit());
        }

        [TestMethod]
        public void GetAuditHistory_ReturnsDescending()
        {
            var a1 = _service.StartAudit("Alice");
            _service.CompleteAudit(a1.Id);
            var a2 = _service.StartAudit("Bob");
            _service.CompleteAudit(a2.Id);
            var history = _service.GetAuditHistory();
            Assert.AreEqual(2, history.Count);
            Assert.IsTrue(history[0].StartedAt >= history[1].StartedAt);
        }

        // ─── Recording Scans ───

        [TestMethod]
        public void RecordScan_ValidEntry_AddedToAudit()
        {
            var audit = _service.StartAudit("Alice");
            var scan = _service.RecordScan(audit.Id, 1, 3, ShelfZone.Action);
            Assert.AreEqual(1, scan.MovieId);
            Assert.AreEqual(3, scan.PhysicalCount);
            Assert.AreEqual(1, audit.Scans.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void RecordScan_InvalidAudit_Throws()
        {
            _service.RecordScan(999, 1, 3, ShelfZone.Action);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void RecordScan_InvalidMovie_Throws()
        {
            var audit = _service.StartAudit("Alice");
            _service.RecordScan(audit.Id, 999, 3, ShelfZone.Action);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void RecordScan_NegativeCount_Throws()
        {
            var audit = _service.StartAudit("Alice");
            _service.RecordScan(audit.Id, 1, -1, ShelfZone.Action);
        }

        [TestMethod]
        public void RecordScan_DuplicateMovie_ReplacesEntry()
        {
            var audit = _service.StartAudit("Alice");
            _service.RecordScan(audit.Id, 1, 2, ShelfZone.Action);
            _service.RecordScan(audit.Id, 1, 5, ShelfZone.Action);
            Assert.AreEqual(1, audit.Scans.Count);
            Assert.AreEqual(5, audit.Scans[0].PhysicalCount);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void RecordScan_CompletedAudit_Throws()
        {
            var audit = _service.StartAudit("Alice");
            _service.CompleteAudit(audit.Id);
            _service.RecordScan(audit.Id, 1, 3, ShelfZone.Action);
        }

        [TestMethod]
        public void RecordScan_CustomScannedBy()
        {
            var audit = _service.StartAudit("Alice");
            var scan = _service.RecordScan(audit.Id, 1, 2, ShelfZone.Action, "Bob");
            Assert.AreEqual("Bob", scan.ScannedBy);
        }

        // ─── Complete / Cancel ───

        [TestMethod]
        public void CompleteAudit_SetsStatusAndDetectsDiscrepancies()
        {
            var audit = _service.StartAudit("Alice");
            _service.RecordScan(audit.Id, 1, 3, ShelfZone.Action); // matches default 3
            _service.RecordScan(audit.Id, 2, 1, ShelfZone.Family); // shortage
            var result = _service.CompleteAudit(audit.Id);
            Assert.AreEqual(AuditStatus.Completed, result.Status);
            Assert.IsNotNull(result.CompletedAt);
            Assert.IsTrue(result.Discrepancies.Count > 0);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void CompleteAudit_AlreadyCompleted_Throws()
        {
            var audit = _service.StartAudit("Alice");
            _service.CompleteAudit(audit.Id);
            _service.CompleteAudit(audit.Id);
        }

        [TestMethod]
        public void CancelAudit_SetsStatusCancelled()
        {
            var audit = _service.StartAudit("Alice");
            var result = _service.CancelAudit(audit.Id);
            Assert.AreEqual(AuditStatus.Cancelled, result.Status);
            Assert.IsNotNull(result.CompletedAt);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void CancelAudit_AlreadyCompleted_Throws()
        {
            var audit = _service.StartAudit("Alice");
            _service.CompleteAudit(audit.Id);
            _service.CancelAudit(audit.Id);
        }

        // ─── Discrepancy Detection ───

        [TestMethod]
        public void Discrepancy_Match_WhenCountsEqual()
        {
            var audit = _service.StartAudit("Alice");
            _service.RecordScan(audit.Id, 1, 3, ShelfZone.Action);
            _service.CompleteAudit(audit.Id);
            var d = audit.Discrepancies.First(x => x.MovieId == 1);
            Assert.AreEqual(DiscrepancyType.Match, d.Type);
            Assert.AreEqual(0, d.Variance);
            Assert.AreEqual(DiscrepancySeverity.None, d.Severity);
        }

        [TestMethod]
        public void Discrepancy_Shortage_WhenPhysicalLess()
        {
            var audit = _service.StartAudit("Alice");
            _service.RecordScan(audit.Id, 1, 1, ShelfZone.Action);
            _service.CompleteAudit(audit.Id);
            var d = audit.Discrepancies.First(x => x.MovieId == 1);
            Assert.AreEqual(DiscrepancyType.Shortage, d.Type);
            Assert.AreEqual(-2, d.Variance);
        }

        [TestMethod]
        public void Discrepancy_Surplus_WhenPhysicalMore()
        {
            var audit = _service.StartAudit("Alice");
            _service.RecordScan(audit.Id, 1, 5, ShelfZone.Action);
            _service.CompleteAudit(audit.Id);
            var d = audit.Discrepancies.First(x => x.MovieId == 1);
            Assert.AreEqual(DiscrepancyType.Surplus, d.Type);
            Assert.AreEqual(2, d.Variance);
        }

        [TestMethod]
        public void Discrepancy_CriticalSeverity_WhenAllMissing()
        {
            var audit = _service.StartAudit("Alice");
            _service.RecordScan(audit.Id, 1, 0, ShelfZone.Action);
            _service.CompleteAudit(audit.Id);
            var d = audit.Discrepancies.First(x => x.MovieId == 1);
            Assert.AreEqual(DiscrepancySeverity.Critical, d.Severity);
        }

        [TestMethod]
        public void Discrepancy_AccountsForActiveRentals()
        {
            // 1 copy rented, so expected on shelf = 2
            _rentalRepo.Add(new Rental { MovieId = 1, CustomerId = 1, RentalDate = DateTime.Today, DueDate = DateTime.Today.AddDays(3), Status = RentalStatus.Active });
            var audit = _service.StartAudit("Alice");
            _service.RecordScan(audit.Id, 1, 2, ShelfZone.Action);
            _service.CompleteAudit(audit.Id);
            var d = audit.Discrepancies.First(x => x.MovieId == 1);
            Assert.AreEqual(DiscrepancyType.Match, d.Type);
            Assert.AreEqual(1, d.ActiveRentals);
        }

        [TestMethod]
        public void Discrepancy_Mislocated_DifferentZone()
        {
            _service.AssignShelfLocation(1, ShelfZone.Action);
            var audit = _service.StartAudit("Alice");
            _service.RecordScan(audit.Id, 1, 3, ShelfZone.Comedy); // wrong zone
            _service.CompleteAudit(audit.Id);
            var d = audit.Discrepancies.First(x => x.MovieId == 1);
            Assert.IsTrue(d.IsMislocated);
            Assert.AreEqual(ShelfZone.Action, d.ExpectedZone);
            Assert.AreEqual(ShelfZone.Comedy, d.ActualZone);
        }

        [TestMethod]
        public void Discrepancy_NotScanned_FlaggedForAuditedZone()
        {
            _service.AssignShelfLocation(1, ShelfZone.Action);
            var audit = _service.StartAudit("Alice", new List<ShelfZone> { ShelfZone.Action });
            // Don't scan movie 1
            _service.CompleteAudit(audit.Id);
            var d = audit.Discrepancies.FirstOrDefault(x => x.MovieId == 1);
            Assert.IsNotNull(d);
            Assert.AreEqual(DiscrepancyType.NotScanned, d.Type);
        }

        // ─── Report Generation ───

        [TestMethod]
        public void GenerateReport_CompletedAudit_ReturnsReport()
        {
            var audit = _service.StartAudit("Alice");
            _service.RecordScan(audit.Id, 1, 3, ShelfZone.Action);
            _service.RecordScan(audit.Id, 2, 1, ShelfZone.Family);
            _service.RecordScan(audit.Id, 3, 3, ShelfZone.SciFi);
            _service.CompleteAudit(audit.Id);

            var report = _service.GenerateReport(audit.Id);
            Assert.AreEqual(audit.Id, report.AuditId);
            Assert.AreEqual("Alice", report.AuditorName);
            Assert.AreEqual(3, report.TitlesScanned);
            Assert.IsTrue(report.ShortageCount > 0);
            Assert.IsTrue(report.ShrinkageUnits > 0);
            Assert.IsTrue(report.EstimatedShrinkageCost > 0);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void GenerateReport_InProgressAudit_Throws()
        {
            var audit = _service.StartAudit("Alice");
            _service.GenerateReport(audit.Id);
        }

        [TestMethod]
        public void GenerateReport_ZoneBreakdown_Populated()
        {
            _service.AssignShelfLocation(1, ShelfZone.Action);
            _service.AssignShelfLocation(2, ShelfZone.Family);
            var audit = _service.StartAudit("Alice");
            _service.RecordScan(audit.Id, 1, 3, ShelfZone.Action);
            _service.RecordScan(audit.Id, 2, 2, ShelfZone.Family);
            _service.CompleteAudit(audit.Id);
            var report = _service.GenerateReport(audit.Id);
            Assert.IsTrue(report.ZoneBreakdown.Count >= 2);
        }

        [TestMethod]
        public void GenerateReport_ShrinkageTrends_MultipleAudits()
        {
            var a1 = _service.StartAudit("Alice");
            _service.RecordScan(a1.Id, 1, 2, ShelfZone.Action);
            _service.CompleteAudit(a1.Id);

            var a2 = _service.StartAudit("Bob");
            _service.RecordScan(a2.Id, 1, 1, ShelfZone.Action);
            _service.CompleteAudit(a2.Id);

            var report = _service.GenerateReport(a2.Id);
            Assert.AreEqual(2, report.ShrinkageTrends.Count);
        }

        // ─── Spot Check ───

        [TestMethod]
        public void SpotCheck_Match()
        {
            var result = _service.SpotCheck(1, 3, ShelfZone.Action);
            Assert.AreEqual(DiscrepancyType.Match, result.Type);
        }

        [TestMethod]
        public void SpotCheck_Shortage()
        {
            var result = _service.SpotCheck(1, 1, ShelfZone.Action);
            Assert.AreEqual(DiscrepancyType.Shortage, result.Type);
        }

        [TestMethod]
        public void SpotCheck_Surplus()
        {
            var result = _service.SpotCheck(1, 5, ShelfZone.Action);
            Assert.AreEqual(DiscrepancyType.Surplus, result.Type);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void SpotCheck_InvalidMovie_Throws()
        {
            _service.SpotCheck(999, 3, ShelfZone.Action);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void SpotCheck_NegativeCount_Throws()
        {
            _service.SpotCheck(1, -1, ShelfZone.Action);
        }

        [TestMethod]
        public void SpotCheck_DetectsMislocation()
        {
            _service.AssignShelfLocation(1, ShelfZone.Action);
            var result = _service.SpotCheck(1, 3, ShelfZone.Comedy);
            Assert.IsTrue(result.IsMislocated);
        }

        // ─── Utility ───

        [TestMethod]
        public void GetUnlocatedMovies_ReturnsUnassigned()
        {
            _service.AssignShelfLocation(1, ShelfZone.Action);
            var unlocated = _service.GetUnlocatedMovies();
            Assert.AreEqual(3, unlocated.Count);
            Assert.IsFalse(unlocated.Any(m => m.Id == 1));
        }

        [TestMethod]
        public void GetStaleAuditMovies_ReturnsUnaudited()
        {
            var audit = _service.StartAudit("Alice");
            _service.RecordScan(audit.Id, 1, 3, ShelfZone.Action);
            _service.CompleteAudit(audit.Id);
            var stale = _service.GetStaleAuditMovies(30);
            Assert.AreEqual(3, stale.Count); // movies 2,3,4 not audited
            Assert.IsFalse(stale.Any(m => m.Id == 1));
        }

        [TestMethod]
        public void AuditModel_TitlesScanned_Correct()
        {
            var audit = _service.StartAudit("Alice");
            _service.RecordScan(audit.Id, 1, 3, ShelfZone.Action);
            _service.RecordScan(audit.Id, 2, 2, ShelfZone.Family);
            Assert.AreEqual(2, audit.TitlesScanned);
        }

        [TestMethod]
        public void AuditModel_TotalCounted_Correct()
        {
            var audit = _service.StartAudit("Alice");
            _service.RecordScan(audit.Id, 1, 3, ShelfZone.Action);
            _service.RecordScan(audit.Id, 2, 2, ShelfZone.Family);
            Assert.AreEqual(5, audit.TotalCounted);
        }

        [TestMethod]
        public void StartAudit_AfterComplete_AllowsNew()
        {
            var a1 = _service.StartAudit("Alice");
            _service.CompleteAudit(a1.Id);
            var a2 = _service.StartAudit("Bob");
            Assert.AreEqual(2, a2.Id);
        }

        [TestMethod]
        public void StartAudit_AfterCancel_AllowsNew()
        {
            var a1 = _service.StartAudit("Alice");
            _service.CancelAudit(a1.Id);
            var a2 = _service.StartAudit("Bob");
            Assert.AreEqual(2, a2.Id);
        }

        [TestMethod]
        public void AccuracyRate_AllMatch_Is100Percent()
        {
            var audit = _service.StartAudit("Alice");
            _service.RecordScan(audit.Id, 1, 3, ShelfZone.Action);
            _service.RecordScan(audit.Id, 2, 3, ShelfZone.Family);
            _service.CompleteAudit(audit.Id);
            var report = _service.GenerateReport(audit.Id);
            Assert.AreEqual(1.0, report.AccuracyRate, 0.001);
        }

        [TestMethod]
        public void Report_TopShortages_OrderedByVariance()
        {
            _inventoryService.SetStock(1, 5);
            _inventoryService.SetStock(2, 10);
            var audit = _service.StartAudit("Alice");
            _service.RecordScan(audit.Id, 1, 2, ShelfZone.Action); // -3
            _service.RecordScan(audit.Id, 2, 3, ShelfZone.Family); // -7
            _service.CompleteAudit(audit.Id);
            var report = _service.GenerateReport(audit.Id);
            Assert.AreEqual(2, report.TopShortages[0].MovieId); // largest shortage first
        }

        [TestMethod]
        public void Discrepancy_OrderedBySeverityDescending()
        {
            var audit = _service.StartAudit("Alice");
            _service.RecordScan(audit.Id, 1, 0, ShelfZone.Action);  // Critical
            _service.RecordScan(audit.Id, 2, 3, ShelfZone.Family);  // Match
            _service.RecordScan(audit.Id, 3, 2, ShelfZone.SciFi);   // Low
            _service.CompleteAudit(audit.Id);
            var sev = audit.Discrepancies
                .Where(d => d.Type != DiscrepancyType.NotScanned)
                .Select(d => d.Severity)
                .ToList();
            // Critical should be first
            Assert.AreEqual(DiscrepancySeverity.Critical, sev[0]);
        }

        [TestMethod]
        public void CustomStockOverride_AffectsDiscrepancy()
        {
            _inventoryService.SetStock(1, 10);
            var audit = _service.StartAudit("Alice");
            _service.RecordScan(audit.Id, 1, 10, ShelfZone.Action);
            _service.CompleteAudit(audit.Id);
            var d = audit.Discrepancies.First(x => x.MovieId == 1);
            Assert.AreEqual(DiscrepancyType.Match, d.Type);
            Assert.AreEqual(10, d.SystemTotal);
        }

        [TestMethod]
        public void RecordScan_WithNotes_Stored()
        {
            var audit = _service.StartAudit("Alice");
            var scan = _service.RecordScan(audit.Id, 1, 3, ShelfZone.Action, notes: "Dusty shelf");
            Assert.AreEqual("Dusty shelf", scan.Notes);
        }
    }
}
