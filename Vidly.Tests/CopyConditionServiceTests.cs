using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class CopyConditionServiceTests
    {
        private InMemoryMovieRepository _movieRepo;
        private InMemoryRentalRepository _rentalRepo;
        private CopyConditionService _service;

        [TestInitialize]
        public void Setup()
        {
            _movieRepo = new InMemoryMovieRepository();
            _rentalRepo = new InMemoryRentalRepository();
            _service = new CopyConditionService(_movieRepo, _rentalRepo);
        }

        private int AddMovie(string name = "Test Movie")
        {
            var movie = new Movie { Name = name, Genre = Genre.Action };
            _movieRepo.Add(movie);
            return movie.Id;
        }

        private int AddRental(int movieId, int customerId = 1)
        {
            var rental = new Rental
            {
                MovieId = movieId,
                CustomerId = customerId,
                RentalDate = DateTime.Today.AddDays(-7),
                DueDate = DateTime.Today,
                DailyRate = 3.99m,
                Status = RentalStatus.Active
            };
            _rentalRepo.Add(rental);
            return rental.Id;
        }

        // ── Constructor ─────────────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new CopyConditionService(null, _rentalRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new CopyConditionService(_movieRepo, null);
        }

        // ── RecordCheckout ──────────────────────────────────────────

        [TestMethod]
        public void RecordCheckout_ValidInput_ReturnsInspection()
        {
            var movieId = AddMovie();
            var rentalId = AddRental(movieId);

            var insp = _service.RecordCheckout(movieId, rentalId, 1,
                ConditionGrade.Good, ConditionGrade.Mint, "Staff A");

            Assert.AreEqual(movieId, insp.MovieId);
            Assert.AreEqual(rentalId, insp.RentalId);
            Assert.AreEqual(InspectionType.Checkout, insp.Type);
            Assert.AreEqual(ConditionGrade.Good, insp.DiscGrade);
            Assert.AreEqual(ConditionGrade.Mint, insp.CaseGrade);
            Assert.AreEqual("Staff A", insp.InspectorName);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void RecordCheckout_InvalidMovie_Throws()
        {
            var rentalId = AddRental(AddMovie());
            _service.RecordCheckout(9999, rentalId, 1,
                ConditionGrade.Good, ConditionGrade.Good, "Staff");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void RecordCheckout_InvalidRental_Throws()
        {
            var movieId = AddMovie();
            _service.RecordCheckout(movieId, 9999, 1,
                ConditionGrade.Good, ConditionGrade.Good, "Staff");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void RecordCheckout_EmptyInspector_Throws()
        {
            var movieId = AddMovie();
            var rentalId = AddRental(movieId);
            _service.RecordCheckout(movieId, rentalId, 1,
                ConditionGrade.Good, ConditionGrade.Good, "");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void RecordCheckout_Duplicate_Throws()
        {
            var movieId = AddMovie();
            var rentalId = AddRental(movieId);
            _service.RecordCheckout(movieId, rentalId, 1,
                ConditionGrade.Good, ConditionGrade.Good, "Staff");
            _service.RecordCheckout(movieId, rentalId, 1,
                ConditionGrade.Good, ConditionGrade.Good, "Staff");
        }

        // ── RecordReturn ────────────────────────────────────────────

        [TestMethod]
        public void RecordReturn_ValidInput_ReturnsInspection()
        {
            var movieId = AddMovie();
            var rentalId = AddRental(movieId);

            var insp = _service.RecordReturn(movieId, rentalId, 1,
                ConditionGrade.Fair, ConditionGrade.Good, "Staff B",
                "Light scratches");

            Assert.AreEqual(InspectionType.Return, insp.Type);
            Assert.AreEqual(ConditionGrade.Fair, insp.DiscGrade);
            Assert.AreEqual("Light scratches", insp.Notes);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void RecordReturn_Duplicate_Throws()
        {
            var movieId = AddMovie();
            var rentalId = AddRental(movieId);
            _service.RecordReturn(movieId, rentalId, 1,
                ConditionGrade.Good, ConditionGrade.Good, "Staff");
            _service.RecordReturn(movieId, rentalId, 1,
                ConditionGrade.Good, ConditionGrade.Good, "Staff");
        }

        // ── RecordAudit ─────────────────────────────────────────────

        [TestMethod]
        public void RecordAudit_ValidInput_ReturnsInspection()
        {
            var movieId = AddMovie();
            var insp = _service.RecordAudit(movieId,
                ConditionGrade.Fair, ConditionGrade.Fair, "Auditor",
                "Annual inventory check");

            Assert.AreEqual(InspectionType.Audit, insp.Type);
            Assert.AreEqual(0, insp.RentalId);
            Assert.AreEqual(0, insp.CustomerId);
        }

        // ── GetRentalDelta ──────────────────────────────────────────

        [TestMethod]
        public void GetRentalDelta_WithBothInspections_ReturnsDelta()
        {
            var movieId = AddMovie();
            var rentalId = AddRental(movieId);
            _service.RecordCheckout(movieId, rentalId, 1,
                ConditionGrade.Good, ConditionGrade.Mint, "Staff");
            _service.RecordReturn(movieId, rentalId, 1,
                ConditionGrade.Fair, ConditionGrade.Good, "Staff");

            var delta = _service.GetRentalDelta(rentalId);

            Assert.IsNotNull(delta);
            Assert.AreEqual(ConditionGrade.Good, delta.DiscBefore);
            Assert.AreEqual(ConditionGrade.Fair, delta.DiscAfter);
            Assert.AreEqual(-1, delta.DiscChange);
            Assert.IsTrue(delta.Deteriorated);
        }

        [TestMethod]
        public void GetRentalDelta_OnlyCheckout_ReturnsNull()
        {
            var movieId = AddMovie();
            var rentalId = AddRental(movieId);
            _service.RecordCheckout(movieId, rentalId, 1,
                ConditionGrade.Good, ConditionGrade.Good, "Staff");

            Assert.IsNull(_service.GetRentalDelta(rentalId));
        }

        [TestMethod]
        public void GetRentalDelta_NoDeterioration_NotFlagged()
        {
            var movieId = AddMovie();
            var rentalId = AddRental(movieId);
            _service.RecordCheckout(movieId, rentalId, 1,
                ConditionGrade.Good, ConditionGrade.Good, "Staff");
            _service.RecordReturn(movieId, rentalId, 1,
                ConditionGrade.Good, ConditionGrade.Good, "Staff");

            var delta = _service.GetRentalDelta(rentalId);
            Assert.IsFalse(delta.Deteriorated);
            Assert.AreEqual(0, delta.DiscChange);
            Assert.AreEqual(0, delta.CaseChange);
        }

        // ── GetDeteriorationHistory ─────────────────────────────────

        [TestMethod]
        public void GetDeteriorationHistory_ReturnsDamagedRentalsOnly()
        {
            var movieId = AddMovie();

            // Rental 1: no damage
            var r1 = AddRental(movieId);
            _service.RecordCheckout(movieId, r1, 1,
                ConditionGrade.Mint, ConditionGrade.Mint, "Staff");
            _service.RecordReturn(movieId, r1, 1,
                ConditionGrade.Mint, ConditionGrade.Mint, "Staff");

            // Rental 2: disc damage
            var r2 = AddRental(movieId, 2);
            _service.RecordCheckout(movieId, r2, 2,
                ConditionGrade.Mint, ConditionGrade.Mint, "Staff");
            _service.RecordReturn(movieId, r2, 2,
                ConditionGrade.Fair, ConditionGrade.Mint, "Staff");

            var history = _service.GetDeteriorationHistory(movieId);
            Assert.AreEqual(1, history.Count);
            Assert.AreEqual(r2, history[0].RentalId);
        }

        // ── GetCopyStatus ───────────────────────────────────────────

        [TestMethod]
        public void GetCopyStatus_NoInspections_ReturnsMint()
        {
            var movieId = AddMovie("Fresh Copy");
            var status = _service.GetCopyStatus(movieId);

            Assert.AreEqual(ConditionGrade.Mint, status.CurrentDiscGrade);
            Assert.AreEqual(ConditionGrade.Mint, status.CurrentCaseGrade);
            Assert.AreEqual(0, status.TotalRentals);
            Assert.IsFalse(status.NeedsReplacement);
        }

        [TestMethod]
        public void GetCopyStatus_AfterReturn_ReflectsLatestCondition()
        {
            var movieId = AddMovie();
            var r1 = AddRental(movieId);
            _service.RecordCheckout(movieId, r1, 1,
                ConditionGrade.Mint, ConditionGrade.Mint, "Staff");
            _service.RecordReturn(movieId, r1, 1,
                ConditionGrade.Fair, ConditionGrade.Good, "Staff");

            var status = _service.GetCopyStatus(movieId);
            Assert.AreEqual(ConditionGrade.Fair, status.CurrentDiscGrade);
            Assert.AreEqual(ConditionGrade.Good, status.CurrentCaseGrade);
            Assert.AreEqual(1, status.TotalRentals);
        }

        [TestMethod]
        public void GetCopyStatus_NeedsReplacement_WhenPoor()
        {
            var movieId = AddMovie();
            var r1 = AddRental(movieId);
            _service.RecordCheckout(movieId, r1, 1,
                ConditionGrade.Good, ConditionGrade.Good, "Staff");
            _service.RecordReturn(movieId, r1, 1,
                ConditionGrade.Poor, ConditionGrade.Good, "Staff");

            var status = _service.GetCopyStatus(movieId);
            Assert.IsTrue(status.NeedsReplacement);
        }

        [TestMethod]
        public void GetCopyStatus_DeteriorationRate_Calculated()
        {
            var movieId = AddMovie();

            // Rental 1: drop by 1
            var r1 = AddRental(movieId);
            _service.RecordCheckout(movieId, r1, 1,
                ConditionGrade.Mint, ConditionGrade.Mint, "Staff");
            _service.RecordReturn(movieId, r1, 1,
                ConditionGrade.Good, ConditionGrade.Mint, "Staff");

            // Rental 2: drop by 1
            var r2 = AddRental(movieId, 2);
            _service.RecordCheckout(movieId, r2, 2,
                ConditionGrade.Good, ConditionGrade.Good, "Staff");
            _service.RecordReturn(movieId, r2, 2,
                ConditionGrade.Fair, ConditionGrade.Good, "Staff");

            var status = _service.GetCopyStatus(movieId);
            Assert.AreEqual(2, status.TotalRentals);
            Assert.IsTrue(status.DeteriorationRate < 0);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetCopyStatus_InvalidMovie_Throws()
        {
            _service.GetCopyStatus(9999);
        }

        // ── GetCopiesNeedingReplacement ─────────────────────────────

        [TestMethod]
        public void GetCopiesNeedingReplacement_ReturnsOnlyDamaged()
        {
            var m1 = AddMovie("Good Movie");
            var m2 = AddMovie("Damaged Movie");

            var r1 = AddRental(m1);
            _service.RecordCheckout(m1, r1, 1,
                ConditionGrade.Good, ConditionGrade.Good, "Staff");
            _service.RecordReturn(m1, r1, 1,
                ConditionGrade.Good, ConditionGrade.Good, "Staff");

            var r2 = AddRental(m2);
            _service.RecordCheckout(m2, r2, 1,
                ConditionGrade.Fair, ConditionGrade.Fair, "Staff");
            _service.RecordReturn(m2, r2, 1,
                ConditionGrade.Damaged, ConditionGrade.Poor, "Staff");

            var needing = _service.GetCopiesNeedingReplacement();
            Assert.AreEqual(1, needing.Count);
            Assert.AreEqual(m2, needing[0].MovieId);
        }

        // ── Renter risk profiles ────────────────────────────────────

        [TestMethod]
        public void GetRenterProfile_NoDamage_LowRisk()
        {
            var movieId = AddMovie();
            var r1 = AddRental(movieId);
            _service.RecordCheckout(movieId, r1, 1,
                ConditionGrade.Good, ConditionGrade.Good, "Staff");
            _service.RecordReturn(movieId, r1, 1,
                ConditionGrade.Good, ConditionGrade.Good, "Staff");

            var profile = _service.GetRenterProfile(1);
            Assert.AreEqual(RenterRiskLevel.Low, profile.RiskLevel);
            Assert.AreEqual(0, profile.DamageEvents);
        }

        [TestMethod]
        public void GetRenterProfile_FrequentDamage_HighRisk()
        {
            var m1 = AddMovie("Movie 1");
            var m2 = AddMovie("Movie 2");
            var m3 = AddMovie("Movie 3");

            // Damage all three rentals
            foreach (var mid in new[] { m1, m2, m3 })
            {
                var rid = AddRental(mid, 5);
                _service.RecordCheckout(mid, rid, 5,
                    ConditionGrade.Good, ConditionGrade.Good, "Staff");
                _service.RecordReturn(mid, rid, 5,
                    ConditionGrade.Fair, ConditionGrade.Good, "Staff");
            }

            var profile = _service.GetRenterProfile(5);
            Assert.AreEqual(RenterRiskLevel.High, profile.RiskLevel);
            Assert.AreEqual(3, profile.DamageEvents);
            Assert.AreEqual(1.0, profile.DamageRate);
        }

        [TestMethod]
        public void GetRenterProfile_NoHistory_LowRisk()
        {
            var profile = _service.GetRenterProfile(999);
            Assert.AreEqual(RenterRiskLevel.Low, profile.RiskLevel);
            Assert.AreEqual(0, profile.TotalRentals);
        }

        [TestMethod]
        public void GetHighRiskRenters_ReturnsOnlyHighRisk()
        {
            var m1 = AddMovie("M1");
            var m2 = AddMovie("M2");

            // Customer 1: careful (no damage)
            var r1 = AddRental(m1, 1);
            _service.RecordCheckout(m1, r1, 1,
                ConditionGrade.Good, ConditionGrade.Good, "Staff");
            _service.RecordReturn(m1, r1, 1,
                ConditionGrade.Good, ConditionGrade.Good, "Staff");

            // Customer 2: careless (always damages)
            var r2 = AddRental(m2, 2);
            _service.RecordCheckout(m2, r2, 2,
                ConditionGrade.Good, ConditionGrade.Good, "Staff");
            _service.RecordReturn(m2, r2, 2,
                ConditionGrade.Poor, ConditionGrade.Fair, "Staff");

            var highRisk = _service.GetHighRiskRenters();
            Assert.AreEqual(1, highRisk.Count);
            Assert.AreEqual(2, highRisk[0].CustomerId);
        }

        // ── Report generation ───────────────────────────────────────

        [TestMethod]
        public void GenerateReport_EmptyInventory_ReturnsZeroes()
        {
            var report = _service.GenerateReport();
            Assert.AreEqual(0, report.TotalCopies);
            Assert.AreEqual(0, report.NeedingReplacement);
        }

        [TestMethod]
        public void GenerateReport_WithData_PopulatesAll()
        {
            var m1 = AddMovie("M1");
            var m2 = AddMovie("M2");

            var r1 = AddRental(m1);
            _service.RecordCheckout(m1, r1, 1,
                ConditionGrade.Mint, ConditionGrade.Mint, "Staff");
            _service.RecordReturn(m1, r1, 1,
                ConditionGrade.Good, ConditionGrade.Good, "Staff");

            var r2 = AddRental(m2);
            _service.RecordCheckout(m2, r2, 2,
                ConditionGrade.Good, ConditionGrade.Good, "Staff");
            _service.RecordReturn(m2, r2, 2,
                ConditionGrade.Damaged, ConditionGrade.Poor, "Staff");

            var report = _service.GenerateReport();
            Assert.AreEqual(2, report.TotalCopies);
            Assert.AreEqual(1, report.NeedingReplacement);
            Assert.AreEqual(1, report.GoodCount);
            Assert.AreEqual(1, report.DamagedCount);
            Assert.IsTrue(report.WorstCopies.Count > 0);
            Assert.IsTrue(report.GeneratedAt <= DateTime.Now);
        }

        // ── Inspection history ──────────────────────────────────────

        [TestMethod]
        public void GetInspectionHistory_ReturnsOrderedByDate()
        {
            var movieId = AddMovie();
            var r1 = AddRental(movieId);
            _service.RecordCheckout(movieId, r1, 1,
                ConditionGrade.Mint, ConditionGrade.Mint, "Staff A");
            _service.RecordReturn(movieId, r1, 1,
                ConditionGrade.Good, ConditionGrade.Mint, "Staff B");

            var history = _service.GetInspectionHistory(movieId);
            Assert.AreEqual(2, history.Count);
            // Most recent first
            Assert.IsTrue(history[0].InspectedAt >= history[1].InspectedAt);
        }

        [TestMethod]
        public void GetRentalInspections_ReturnsBoth()
        {
            var movieId = AddMovie();
            var rentalId = AddRental(movieId);
            _service.RecordCheckout(movieId, rentalId, 1,
                ConditionGrade.Good, ConditionGrade.Good, "Staff");
            _service.RecordReturn(movieId, rentalId, 1,
                ConditionGrade.Fair, ConditionGrade.Good, "Staff");

            var inspections = _service.GetRentalInspections(rentalId);
            Assert.AreEqual(2, inspections.Count);
            Assert.AreEqual(InspectionType.Checkout, inspections[0].Type);
            Assert.AreEqual(InspectionType.Return, inspections[1].Type);
        }

        [TestMethod]
        public void GetInspectionCount_TracksTotal()
        {
            var movieId = AddMovie();
            var rentalId = AddRental(movieId);
            Assert.AreEqual(0, _service.GetInspectionCount());

            _service.RecordCheckout(movieId, rentalId, 1,
                ConditionGrade.Good, ConditionGrade.Good, "Staff");
            Assert.AreEqual(1, _service.GetInspectionCount());

            _service.RecordReturn(movieId, rentalId, 1,
                ConditionGrade.Good, ConditionGrade.Good, "Staff");
            Assert.AreEqual(2, _service.GetInspectionCount());

            _service.RecordAudit(movieId,
                ConditionGrade.Good, ConditionGrade.Good, "Auditor");
            Assert.AreEqual(3, _service.GetInspectionCount());
        }

        // ── RentalConditionDelta computed properties ────────────────

        [TestMethod]
        public void RentalConditionDelta_CaseDeterioration_Flagged()
        {
            var movieId = AddMovie();
            var rentalId = AddRental(movieId);
            _service.RecordCheckout(movieId, rentalId, 1,
                ConditionGrade.Good, ConditionGrade.Mint, "Staff");
            _service.RecordReturn(movieId, rentalId, 1,
                ConditionGrade.Good, ConditionGrade.Fair, "Staff");

            var delta = _service.GetRentalDelta(rentalId);
            Assert.AreEqual(0, delta.DiscChange);
            Assert.AreEqual(-2, delta.CaseChange);
            Assert.IsTrue(delta.Deteriorated);
        }

        // ── Medium risk threshold ───────────────────────────────────

        [TestMethod]
        public void GetRenterProfile_SomeDamage_MediumRisk()
        {
            // 1 out of 5 rentals damaged = 20% → Medium risk
            var movies = new int[5];
            for (int i = 0; i < 5; i++)
                movies[i] = AddMovie($"Movie {i}");

            for (int i = 0; i < 5; i++)
            {
                var rid = AddRental(movies[i], 10);
                _service.RecordCheckout(movies[i], rid, 10,
                    ConditionGrade.Good, ConditionGrade.Good, "Staff");

                var returnDisc = i == 0 ? ConditionGrade.Fair : ConditionGrade.Good;
                _service.RecordReturn(movies[i], rid, 10,
                    returnDisc, ConditionGrade.Good, "Staff");
            }

            var profile = _service.GetRenterProfile(10);
            Assert.AreEqual(RenterRiskLevel.Medium, profile.RiskLevel);
        }
    }
}
