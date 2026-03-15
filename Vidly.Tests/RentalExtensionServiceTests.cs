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
    public class RentalExtensionServiceTests
    {
        private RentalExtensionService _service;
        private TestClock _clock;

        [TestInitialize]
        public void Setup()
        {
            InMemoryMovieRepository.Reset();
            InMemoryCustomerRepository.Reset();
            InMemoryRentalRepository.Reset();

            _clock = new TestClock(new DateTime(2025, 3, 15));
        }

        private void CreateService()
        {
            _service = new RentalExtensionService(
                new InMemoryRentalRepository(),
                new InMemoryCustomerRepository(),
                new InMemoryReservationRepository(),
                _clock);
        }

        private Customer AddCustomer(int id, string name,
            MembershipType tier = MembershipType.Basic)
        {
            var repo = new InMemoryCustomerRepository();
            var c = new Customer
            {
                Id = id,
                Name = name,
                MembershipType = tier,
                MemberSince = new DateTime(2024, 1, 1)
            };
            repo.Add(c);
            return c;
        }

        private Movie AddMovie(int id, string name)
        {
            var repo = new InMemoryMovieRepository();
            var m = new Movie
            {
                Id = id,
                Name = name,
                Genre = Genre.Action,
                ReleaseDate = new DateTime(2024, 6, 1)
            };
            repo.Add(m);
            return m;
        }

        private Rental AddRental(int id, int customerId, int movieId,
            DateTime rentalDate, int durationDays = 7,
            RentalStatus status = RentalStatus.Active)
        {
            var repo = new InMemoryRentalRepository();
            var r = new Rental
            {
                Id = id,
                CustomerId = customerId,
                MovieId = movieId,
                RentalDate = rentalDate,
                DueDate = rentalDate.AddDays(durationDays),
                DailyRate = 3.99m,
                Status = status
            };
            repo.Add(r);
            return r;
        }

        // ── RequestExtension ─────────────────────────────────────────

        [TestMethod]
        public void RequestExtension_ValidRental_ExtendsDueDate()
        {
            AddCustomer(100, "Alice");
            AddMovie(200, "The Matrix");
            AddRental(1, 100, 200, new DateTime(2025, 3, 10)); // due Mar 17
            CreateService();

            var result = _service.RequestExtension(1, 3);

            Assert.AreEqual(new DateTime(2025, 3, 20), result.Rental.DueDate);
            Assert.AreEqual(3, result.Extension.DaysAdded);
            Assert.AreEqual(1, result.Extension.ExtensionNumber);
        }

        [TestMethod]
        public void RequestExtension_DefaultDays_UsesDefaultExtensionDays()
        {
            AddCustomer(100, "Alice");
            AddMovie(200, "The Matrix");
            AddRental(1, 100, 200, new DateTime(2025, 3, 10)); // due Mar 17
            CreateService();

            var result = _service.RequestExtension(1);

            Assert.AreEqual(RentalExtensionService.DefaultExtensionDays,
                result.Extension.DaysAdded);
        }

        [TestMethod]
        public void RequestExtension_CalculatesFee_Basic()
        {
            AddCustomer(100, "Alice", MembershipType.Basic);
            AddMovie(200, "The Matrix");
            AddRental(1, 100, 200, new DateTime(2025, 3, 10)); // daily rate 3.99
            CreateService();

            var result = _service.RequestExtension(1, 2);

            // Fee = 3.99 * 0.75 * 2 = 5.985, rounded to 5.99
            var expected = Math.Round(3.99m * 0.75m * 2, 2);
            Assert.AreEqual(expected, result.Extension.Fee);
            Assert.IsFalse(result.WasFreeExtension);
        }

        [TestMethod]
        public void RequestExtension_PlatinumMember_FreeExtension()
        {
            AddCustomer(100, "Alice", MembershipType.Platinum);
            AddMovie(200, "The Matrix");
            AddRental(1, 100, 200, new DateTime(2025, 3, 10));
            CreateService();

            var result = _service.RequestExtension(1, 3);

            Assert.AreEqual(0m, result.Extension.Fee);
            Assert.IsTrue(result.WasFreeExtension);
        }

        [TestMethod]
        public void RequestExtension_GoldMember_HalfDiscount()
        {
            AddCustomer(100, "Alice", MembershipType.Gold);
            AddMovie(200, "The Matrix");
            AddRental(1, 100, 200, new DateTime(2025, 3, 10));
            CreateService();

            var result = _service.RequestExtension(1, 2);

            // Fee = 3.99 * 0.75 * (1 - 0.5) * 2 = 2.9925 -> 2.99
            var expected = Math.Round(3.99m * 0.75m * 0.5m * 2, 2);
            Assert.AreEqual(expected, result.Extension.Fee);
            Assert.AreEqual(0.50m, result.Extension.DiscountApplied);
        }

        [TestMethod]
        public void RequestExtension_SilverMember_QuarterDiscount()
        {
            AddCustomer(100, "Alice", MembershipType.Silver);
            AddMovie(200, "The Matrix");
            AddRental(1, 100, 200, new DateTime(2025, 3, 10));
            CreateService();

            var result = _service.RequestExtension(1, 1);

            // Fee = 3.99 * 0.75 * (1 - 0.25) * 1 = 2.244375 -> 2.24
            var expected = Math.Round(3.99m * 0.75m * 0.75m * 1, 2);
            Assert.AreEqual(expected, result.Extension.Fee);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void RequestExtension_NonexistentRental_Throws()
        {
            CreateService();
            _service.RequestExtension(999);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void RequestExtension_ReturnedRental_Throws()
        {
            AddCustomer(100, "Alice");
            AddMovie(200, "The Matrix");
            AddRental(1, 100, 200, new DateTime(2025, 3, 10),
                status: RentalStatus.Returned);
            CreateService();

            _service.RequestExtension(1);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void RequestExtension_ExceedsMaxDays_Throws()
        {
            AddCustomer(100, "Alice");
            AddMovie(200, "The Matrix");
            AddRental(1, 100, 200, new DateTime(2025, 3, 10));
            CreateService();

            _service.RequestExtension(1, RentalExtensionService.MaxExtensionDays + 1);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void RequestExtension_TooFarOverdue_Throws()
        {
            AddCustomer(100, "Alice");
            AddMovie(200, "The Matrix");
            // Rental due Mar 8, today is Mar 15 — 7 days past due
            AddRental(1, 100, 200, new DateTime(2025, 3, 1));
            CreateService();

            _service.RequestExtension(1);
        }

        [TestMethod]
        public void RequestExtension_SlightlyOverdue_Succeeds()
        {
            AddCustomer(100, "Alice");
            AddMovie(200, "The Matrix");
            // Rental due Mar 14, today Mar 15 — 1 day past due (within limit)
            AddRental(1, 100, 200, new DateTime(2025, 3, 7));
            CreateService();

            var result = _service.RequestExtension(1, 3);
            Assert.IsNotNull(result);
            Assert.AreEqual(3, result.Extension.DaysAdded);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void RequestExtension_MaxExtensionsReached_Throws()
        {
            AddCustomer(100, "Alice");
            AddMovie(200, "The Matrix");
            AddRental(1, 100, 200, new DateTime(2025, 3, 10));
            CreateService();

            for (int i = 0; i < RentalExtensionService.MaxExtensionsPerRental; i++)
                _service.RequestExtension(1, 1);

            // This should throw
            _service.RequestExtension(1, 1);
        }

        [TestMethod]
        public void RequestExtension_TracksRemainingExtensions()
        {
            AddCustomer(100, "Alice");
            AddMovie(200, "The Matrix");
            AddRental(1, 100, 200, new DateTime(2025, 3, 10));
            CreateService();

            var result1 = _service.RequestExtension(1, 1);
            Assert.AreEqual(RentalExtensionService.MaxExtensionsPerRental - 1,
                result1.RemainingExtensions);

            var result2 = _service.RequestExtension(1, 1);
            Assert.AreEqual(RentalExtensionService.MaxExtensionsPerRental - 2,
                result2.RemainingExtensions);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void RequestExtension_ReservationConflict_Throws()
        {
            AddCustomer(100, "Alice");
            AddCustomer(101, "Bob");
            AddMovie(200, "The Matrix");
            AddRental(1, 100, 200, new DateTime(2025, 3, 10));

            // Add a reservation for the same movie
            var reservationRepo = new InMemoryReservationRepository();
            reservationRepo.Add(new Reservation
            {
                CustomerId = 101,
                MovieId = 200,
                Status = ReservationStatus.Waiting,
                QueuePosition = 1,
                ReservedDate = DateTime.Today
            });

            _service = new RentalExtensionService(
                new InMemoryRentalRepository(),
                new InMemoryCustomerRepository(),
                reservationRepo,
                _clock);

            _service.RequestExtension(1);
        }

        [TestMethod]
        public void RequestExtension_OverdueRental_ResetsToActive()
        {
            AddCustomer(100, "Alice");
            AddMovie(200, "The Matrix");
            // Due Mar 13, today Mar 15 — 2 days overdue
            var rental = AddRental(1, 100, 200, new DateTime(2025, 3, 6),
                status: RentalStatus.Overdue);
            CreateService();

            var result = _service.RequestExtension(1, 5);

            // New due date = Mar 13 + 5 = Mar 18, which is after today (Mar 15)
            Assert.AreEqual(RentalStatus.Active, result.Rental.Status);
        }

        // ── CheckEligibility ─────────────────────────────────────────

        [TestMethod]
        public void CheckEligibility_EligibleRental_ReturnsTrue()
        {
            AddCustomer(100, "Alice");
            AddMovie(200, "The Matrix");
            AddRental(1, 100, 200, new DateTime(2025, 3, 10));
            CreateService();

            var elig = _service.CheckEligibility(1);

            Assert.IsTrue(elig.IsEligible);
            Assert.AreEqual(RentalExtensionService.MaxExtensionsPerRental,
                elig.RemainingExtensions);
            Assert.IsTrue(elig.EstimatedFeePerDay > 0);
        }

        [TestMethod]
        public void CheckEligibility_NonexistentRental_NotEligible()
        {
            CreateService();

            var elig = _service.CheckEligibility(999);

            Assert.IsFalse(elig.IsEligible);
            Assert.IsTrue(elig.Reason.Contains("not found"));
        }

        [TestMethod]
        public void CheckEligibility_ReturnedRental_NotEligible()
        {
            AddCustomer(100, "Alice");
            AddMovie(200, "The Matrix");
            AddRental(1, 100, 200, new DateTime(2025, 3, 10),
                status: RentalStatus.Returned);
            CreateService();

            var elig = _service.CheckEligibility(1);

            Assert.IsFalse(elig.IsEligible);
            Assert.IsTrue(elig.Reason.Contains("returned"));
        }

        [TestMethod]
        public void CheckEligibility_TooOverdue_NotEligible()
        {
            AddCustomer(100, "Alice");
            AddMovie(200, "The Matrix");
            AddRental(1, 100, 200, new DateTime(2025, 3, 1)); // due Mar 8
            CreateService();

            var elig = _service.CheckEligibility(1);

            Assert.IsFalse(elig.IsEligible);
            Assert.IsTrue(elig.Reason.Contains("overdue"));
        }

        [TestMethod]
        public void CheckEligibility_MaxExtensionsUsed_NotEligible()
        {
            AddCustomer(100, "Alice");
            AddMovie(200, "The Matrix");
            AddRental(1, 100, 200, new DateTime(2025, 3, 10));
            CreateService();

            for (int i = 0; i < RentalExtensionService.MaxExtensionsPerRental; i++)
                _service.RequestExtension(1, 1);

            var elig = _service.CheckEligibility(1);

            Assert.IsFalse(elig.IsEligible);
            Assert.IsTrue(elig.Reason.Contains("extended"));
        }

        [TestMethod]
        public void CheckEligibility_PlatinumMember_ZeroFee()
        {
            AddCustomer(100, "Alice", MembershipType.Platinum);
            AddMovie(200, "The Matrix");
            AddRental(1, 100, 200, new DateTime(2025, 3, 10));
            CreateService();

            var elig = _service.CheckEligibility(1);

            Assert.IsTrue(elig.IsEligible);
            Assert.AreEqual(0m, elig.EstimatedFeePerDay);
            Assert.AreEqual(1.00m, elig.MembershipDiscount);
        }

        // ── GetExtensionHistory ──────────────────────────────────────

        [TestMethod]
        public void GetExtensionHistory_NoExtensions_ReturnsEmpty()
        {
            CreateService();

            var history = _service.GetExtensionHistory(1);

            Assert.AreEqual(0, history.Count);
        }

        [TestMethod]
        public void GetExtensionHistory_AfterExtensions_ReturnsAll()
        {
            AddCustomer(100, "Alice");
            AddMovie(200, "The Matrix");
            AddRental(1, 100, 200, new DateTime(2025, 3, 10));
            CreateService();

            _service.RequestExtension(1, 2);
            _service.RequestExtension(1, 3);

            var history = _service.GetExtensionHistory(1);

            Assert.AreEqual(2, history.Count);
            Assert.AreEqual(2, history[0].DaysAdded);
            Assert.AreEqual(3, history[1].DaysAdded);
            Assert.AreEqual(1, history[0].ExtensionNumber);
            Assert.AreEqual(2, history[1].ExtensionNumber);
        }

        // ── GetCustomerExtensions ────────────────────────────────────

        [TestMethod]
        public void GetCustomerExtensions_AcrossMultipleRentals()
        {
            AddCustomer(100, "Alice");
            AddMovie(200, "The Matrix");
            AddMovie(201, "Inception");
            AddRental(1, 100, 200, new DateTime(2025, 3, 10));
            AddRental(2, 100, 201, new DateTime(2025, 3, 10));
            CreateService();

            _service.RequestExtension(1, 2);
            _service.RequestExtension(2, 3);

            var exts = _service.GetCustomerExtensions(100);

            Assert.AreEqual(2, exts.Count);
        }

        // ── GetStats ─────────────────────────────────────────────────

        [TestMethod]
        public void GetStats_NoExtensions_ReturnsDefaults()
        {
            CreateService();

            var stats = _service.GetStats();

            Assert.AreEqual(0, stats.TotalExtensions);
            Assert.AreEqual(0, stats.UniqueRentalsExtended);
        }

        [TestMethod]
        public void GetStats_WithExtensions_CalculatesCorrectly()
        {
            AddCustomer(100, "Alice");
            AddMovie(200, "The Matrix");
            AddMovie(201, "Inception");
            AddRental(1, 100, 200, new DateTime(2025, 3, 10));
            AddRental(2, 100, 201, new DateTime(2025, 3, 10));
            CreateService();

            _service.RequestExtension(1, 2);
            _service.RequestExtension(1, 3);
            _service.RequestExtension(2, 1);

            var stats = _service.GetStats();

            Assert.AreEqual(3, stats.TotalExtensions);
            Assert.AreEqual(2, stats.UniqueRentalsExtended);
            Assert.IsTrue(stats.TotalFeesCollected > 0);
            Assert.IsTrue(stats.AverageDaysAdded > 0);
            Assert.AreEqual(2, stats.MaxExtensionsOnSingleRental);
        }

        // ── GetExtensionSummary ──────────────────────────────────────

        [TestMethod]
        public void GetExtensionSummary_NoExtensions_ReturnsMessage()
        {
            AddCustomer(100, "Alice");
            AddMovie(200, "The Matrix");
            AddRental(1, 100, 200, new DateTime(2025, 3, 10));
            CreateService();

            var summary = _service.GetExtensionSummary(1);

            Assert.IsTrue(summary.Contains("no extensions"));
        }

        [TestMethod]
        public void GetExtensionSummary_WithExtensions_ContainsDetails()
        {
            AddCustomer(100, "Alice", MembershipType.Gold);
            AddMovie(200, "The Matrix");
            AddRental(1, 100, 200, new DateTime(2025, 3, 10));
            CreateService();

            _service.RequestExtension(1, 3);

            var summary = _service.GetExtensionSummary(1);

            Assert.IsTrue(summary.Contains("Extension history"));
            Assert.IsTrue(summary.Contains("The Matrix"));
            Assert.IsTrue(summary.Contains("+3 days"));
            Assert.IsTrue(summary.Contains("membership discount"));
        }

        [TestMethod]
        public void GetExtensionSummary_NonexistentRental_ReturnsNotFound()
        {
            CreateService();

            var summary = _service.GetExtensionSummary(999);

            Assert.AreEqual("Rental not found.", summary);
        }
    }
}
