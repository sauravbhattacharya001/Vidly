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
    public class RevenueLeakageServiceTests
    {
        private InMemoryRentalRepository _rentalRepo;
        private InMemoryMovieRepository _movieRepo;
        private InMemoryCustomerRepository _customerRepo;
        private InMemoryGiftCardRepository _giftCardRepo;
        private InMemorySubscriptionRepository _subRepo;
        private TestClock _clock;
        private RevenueLeakageService _service;

        [TestInitialize]
        public void Setup()
        {
            _rentalRepo = new InMemoryRentalRepository();
            _movieRepo = new InMemoryMovieRepository();
            _customerRepo = new InMemoryCustomerRepository();
            _giftCardRepo = new InMemoryGiftCardRepository();
            _subRepo = new InMemorySubscriptionRepository();
            _clock = new TestClock(new DateTime(2026, 4, 15));
            _service = new RevenueLeakageService(
                _rentalRepo, _movieRepo, _customerRepo,
                _giftCardRepo, _subRepo, _clock);
        }

        // ── Constructor ────────────────────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new RevenueLeakageService(null, _movieRepo, _customerRepo, _giftCardRepo, _subRepo, _clock);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new RevenueLeakageService(_rentalRepo, null, _customerRepo, _giftCardRepo, _subRepo, _clock);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustomerRepo_Throws()
        {
            new RevenueLeakageService(_rentalRepo, _movieRepo, null, _giftCardRepo, _subRepo, _clock);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullGiftCardRepo_Throws()
        {
            new RevenueLeakageService(_rentalRepo, _movieRepo, _customerRepo, null, _subRepo, _clock);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullSubscriptionRepo_Throws()
        {
            new RevenueLeakageService(_rentalRepo, _movieRepo, _customerRepo, _giftCardRepo, null, _clock);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullClock_Throws()
        {
            new RevenueLeakageService(_rentalRepo, _movieRepo, _customerRepo, _giftCardRepo, _subRepo, null);
        }

        // ── Empty Data ─────────────────────────────────────────────────────

        [TestMethod]
        public void Analyze_EmptyData_ReturnsValidReport()
        {
            var report = _service.Analyze();

            Assert.IsNotNull(report);
            Assert.AreEqual(8, report.DetectorsRun);
            Assert.AreEqual(0m, report.TotalLeakage);
            Assert.AreEqual(0, report.Leaks.Count);
            Assert.AreEqual("stable", report.Trend);
        }

        // ── Detector 1: Uncollected Late Fees ──────────────────────────────

        [TestMethod]
        public void Analyze_OverdueRentals_DetectsUncollectedLateFees()
        {
            _rentalRepo.Add(new Rental
            {
                CustomerId = 1, MovieId = 1, RentalDate = new DateTime(2026, 3, 1),
                DueDate = new DateTime(2026, 3, 8), DailyRate = 2.99m,
                Status = RentalStatus.Active // overdue by 38 days
            });

            var report = _service.Analyze();
            var leak = report.Leaks.FirstOrDefault(l => l.Category == LeakCategory.UncollectedLateFees);

            Assert.IsNotNull(leak);
            Assert.IsTrue(leak.EstimatedImpact > 0);
            Assert.AreEqual(1, leak.AffectedCount);
            Assert.IsTrue(leak.Confidence >= 0.8);
        }

        [TestMethod]
        public void Analyze_NoOverdueRentals_NoLateFeeLeaks()
        {
            _rentalRepo.Add(new Rental
            {
                CustomerId = 1, MovieId = 1, RentalDate = new DateTime(2026, 4, 10),
                DueDate = new DateTime(2026, 4, 20), DailyRate = 2.99m,
                Status = RentalStatus.Active
            });

            var report = _service.Analyze();
            var leak = report.Leaks.FirstOrDefault(l => l.Category == LeakCategory.UncollectedLateFees);
            Assert.IsNull(leak);
        }

        [TestMethod]
        public void Analyze_ReturnedRental_NotCountedAsOverdue()
        {
            _rentalRepo.Add(new Rental
            {
                CustomerId = 1, MovieId = 1, RentalDate = new DateTime(2026, 3, 1),
                DueDate = new DateTime(2026, 3, 8), ReturnDate = new DateTime(2026, 3, 15),
                DailyRate = 2.99m, Status = RentalStatus.Returned
            });

            var report = _service.Analyze();
            var leak = report.Leaks.FirstOrDefault(l => l.Category == LeakCategory.UncollectedLateFees);
            Assert.IsNull(leak);
        }

        // ── Detector 2: Expired Gift Cards ─────────────────────────────────

        [TestMethod]
        public void Analyze_ExpiredGiftCards_DetectsLostBusiness()
        {
            _giftCardRepo.Add(new GiftCard
            {
                Code = "GIFT-TEST-0001-AAAA",
                OriginalValue = 50m, Balance = 30m, IsActive = true,
                ExpirationDate = new DateTime(2026, 3, 1), // expired
                CreatedDate = new DateTime(2025, 12, 1)
            });

            var report = _service.Analyze();
            var leak = report.Leaks.FirstOrDefault(l => l.Category == LeakCategory.ExpiredGiftCards);

            Assert.IsNotNull(leak);
            Assert.AreEqual(45m, leak.EstimatedImpact); // 30 * 1.5
            Assert.AreEqual(1, leak.AffectedCount);
        }

        [TestMethod]
        public void Analyze_ActiveGiftCards_NoLeak()
        {
            _giftCardRepo.Add(new GiftCard
            {
                Code = "GIFT-TEST-0002-BBBB",
                OriginalValue = 50m, Balance = 30m, IsActive = true,
                ExpirationDate = new DateTime(2027, 1, 1),
                CreatedDate = new DateTime(2026, 1, 1)
            });

            var report = _service.Analyze();
            var leak = report.Leaks.FirstOrDefault(l => l.Category == LeakCategory.ExpiredGiftCards);
            Assert.IsNull(leak);
        }

        [TestMethod]
        public void Analyze_ExpiredGiftCardZeroBalance_NoLeak()
        {
            _giftCardRepo.Add(new GiftCard
            {
                Code = "GIFT-TEST-0003-CCCC",
                OriginalValue = 50m, Balance = 0m, IsActive = true,
                ExpirationDate = new DateTime(2026, 3, 1),
                CreatedDate = new DateTime(2025, 12, 1)
            });

            var report = _service.Analyze();
            var leak = report.Leaks.FirstOrDefault(l => l.Category == LeakCategory.ExpiredGiftCards);
            Assert.IsNull(leak);
        }

        // ── Detector 4: Idle Inventory ─────────────────────────────────────

        [TestMethod]
        public void Analyze_MovieNeverRented_DetectsIdleInventory()
        {
            _movieRepo.Add(new Movie { Name = "Idle Movie", Genre = Genre.Action });

            var report = _service.Analyze();
            var leak = report.Leaks.FirstOrDefault(l => l.Category == LeakCategory.IdleInventory);

            Assert.IsNotNull(leak);
            Assert.AreEqual(1, leak.AffectedCount);
            Assert.IsTrue(leak.EstimatedImpact > 0);
        }

        [TestMethod]
        public void Analyze_RecentlyRentedMovie_NotIdle()
        {
            _movieRepo.Add(new Movie { Name = "Popular Movie", Genre = Genre.Action });
            var movies = _movieRepo.GetAll();
            int movieId = movies[0].Id;

            _rentalRepo.Add(new Rental
            {
                CustomerId = 1, MovieId = movieId,
                RentalDate = new DateTime(2026, 4, 10),
                DueDate = new DateTime(2026, 4, 17),
                DailyRate = 2.99m, Status = RentalStatus.Active
            });

            var report = _service.Analyze();
            var leak = report.Leaks.FirstOrDefault(l => l.Category == LeakCategory.IdleInventory);
            Assert.IsNull(leak);
        }

        // ── Detector 5: Lapsed Subscribers ─────────────────────────────────

        [TestMethod]
        public void Analyze_RecentlyLapsedSubscriber_DetectsLeak()
        {
            _subRepo.Add(new CustomerSubscription
            {
                CustomerId = 1,
                PlanType = SubscriptionPlanType.Standard,
                Status = SubscriptionStatus.Cancelled,
                StartDate = new DateTime(2025, 6, 1),
                CurrentPeriodStart = new DateTime(2026, 3, 1),
                CurrentPeriodEnd = new DateTime(2026, 3, 31),
                CancelledDate = new DateTime(2026, 3, 15),
                TotalBilled = 99.90m
            });

            var report = _service.Analyze();
            var leak = report.Leaks.FirstOrDefault(l => l.Category == LeakCategory.LapsedSubscribers);

            Assert.IsNotNull(leak);
            Assert.AreEqual(1, leak.AffectedCount);
        }

        [TestMethod]
        public void Analyze_LongAgoLapsedSubscriber_NotDetected()
        {
            _subRepo.Add(new CustomerSubscription
            {
                CustomerId = 1,
                PlanType = SubscriptionPlanType.Basic,
                Status = SubscriptionStatus.Cancelled,
                StartDate = new DateTime(2024, 1, 1),
                CurrentPeriodStart = new DateTime(2025, 1, 1),
                CurrentPeriodEnd = new DateTime(2025, 1, 31),
                CancelledDate = new DateTime(2025, 1, 15),
                TotalBilled = 49.90m
            });

            var report = _service.Analyze();
            var leak = report.Leaks.FirstOrDefault(l => l.Category == LeakCategory.LapsedSubscribers);
            Assert.IsNull(leak);
        }

        // ── Detector 6: Underutilized Subscriptions ────────────────────────

        [TestMethod]
        public void Analyze_UnderutilizedSubscription_DetectsChurnRisk()
        {
            _subRepo.Add(new CustomerSubscription
            {
                CustomerId = 1,
                PlanType = SubscriptionPlanType.Standard,
                Status = SubscriptionStatus.Active,
                StartDate = new DateTime(2026, 1, 1),
                CurrentPeriodStart = new DateTime(2026, 4, 1),
                CurrentPeriodEnd = new DateTime(2026, 4, 30),
                RentalsUsedThisPeriod = 0, // 0 of 5
                TotalBilled = 39.96m
            });

            var report = _service.Analyze();
            var leak = report.Leaks.FirstOrDefault(l => l.Category == LeakCategory.UnderutilizedSubscriptions);

            Assert.IsNotNull(leak);
            Assert.AreEqual(1, leak.AffectedCount);
        }

        [TestMethod]
        public void Analyze_WellUtilizedSubscription_NoLeak()
        {
            _subRepo.Add(new CustomerSubscription
            {
                CustomerId = 1,
                PlanType = SubscriptionPlanType.Standard,
                Status = SubscriptionStatus.Active,
                StartDate = new DateTime(2026, 1, 1),
                CurrentPeriodStart = new DateTime(2026, 4, 1),
                CurrentPeriodEnd = new DateTime(2026, 4, 30),
                RentalsUsedThisPeriod = 4, // 4 of 5 = 80%
                TotalBilled = 39.96m
            });

            var report = _service.Analyze();
            var leak = report.Leaks.FirstOrDefault(l => l.Category == LeakCategory.UnderutilizedSubscriptions);
            Assert.IsNull(leak);
        }

        // ── Detector 7: Overdue Unreturned ─────────────────────────────────

        [TestMethod]
        public void Analyze_LongOverdueRental_DetectsWriteOff()
        {
            _rentalRepo.Add(new Rental
            {
                CustomerId = 1, MovieId = 1,
                RentalDate = new DateTime(2026, 2, 1),
                DueDate = new DateTime(2026, 2, 8), // 66 days overdue
                DailyRate = 3.99m, Status = RentalStatus.Overdue
            });

            var report = _service.Analyze();
            var leak = report.Leaks.FirstOrDefault(l => l.Category == LeakCategory.OverdueUnreturned);

            Assert.IsNotNull(leak);
            Assert.AreEqual(LeakSeverity.Critical, leak.Severity);
            Assert.AreEqual(79.80m, leak.EstimatedImpact); // 3.99 * 20
        }

        [TestMethod]
        public void Analyze_RecentlyOverdueRental_NotFlaggedAsWriteOff()
        {
            _rentalRepo.Add(new Rental
            {
                CustomerId = 1, MovieId = 1,
                RentalDate = new DateTime(2026, 4, 1),
                DueDate = new DateTime(2026, 4, 8), // only 7 days overdue
                DailyRate = 3.99m, Status = RentalStatus.Active
            });

            var report = _service.Analyze();
            var leak = report.Leaks.FirstOrDefault(l => l.Category == LeakCategory.OverdueUnreturned);
            Assert.IsNull(leak);
        }

        // ── Detector 8: Dormant Customers ──────────────────────────────────

        [TestMethod]
        public void Analyze_DormantCustomer_DetectsLeak()
        {
            _customerRepo.Add(new Customer
            {
                Name = "Dormant Dave", MemberSince = new DateTime(2025, 1, 1),
                MembershipType = MembershipType.Silver
            });
            var customers = _customerRepo.GetAll();

            _rentalRepo.Add(new Rental
            {
                CustomerId = customers[0].Id, MovieId = 1,
                RentalDate = new DateTime(2025, 6, 1),
                DueDate = new DateTime(2025, 6, 8),
                ReturnDate = new DateTime(2025, 6, 7),
                DailyRate = 2.99m, Status = RentalStatus.Returned
            });

            var report = _service.Analyze();
            var leak = report.Leaks.FirstOrDefault(l => l.Category == LeakCategory.DormantCustomers);

            Assert.IsNotNull(leak);
            Assert.AreEqual(1, leak.AffectedCount);
        }

        [TestMethod]
        public void Analyze_ActiveCustomer_NotDormant()
        {
            _customerRepo.Add(new Customer
            {
                Name = "Active Alice", MemberSince = new DateTime(2025, 1, 1),
                MembershipType = MembershipType.Gold
            });
            var customers = _customerRepo.GetAll();

            _rentalRepo.Add(new Rental
            {
                CustomerId = customers[0].Id, MovieId = 1,
                RentalDate = new DateTime(2026, 4, 10),
                DueDate = new DateTime(2026, 4, 17),
                DailyRate = 2.99m, Status = RentalStatus.Active
            });

            var report = _service.Analyze();
            var leak = report.Leaks.FirstOrDefault(l => l.Category == LeakCategory.DormantCustomers);
            Assert.IsNull(leak);
        }

        [TestMethod]
        public void Analyze_NeverRentedOldCustomer_Dormant()
        {
            _customerRepo.Add(new Customer
            {
                Name = "Never Rented Nancy",
                MemberSince = new DateTime(2025, 1, 1),
                MembershipType = MembershipType.Basic
            });

            var report = _service.Analyze();
            var leak = report.Leaks.FirstOrDefault(l => l.Category == LeakCategory.DormantCustomers);

            Assert.IsNotNull(leak);
            Assert.AreEqual(1, leak.AffectedCount);
        }

        // ── Report Structure ───────────────────────────────────────────────

        [TestMethod]
        public void Analyze_LeaksOrderedByImpactDescending()
        {
            // Create multiple leak conditions
            _rentalRepo.Add(new Rental
            {
                CustomerId = 1, MovieId = 1,
                RentalDate = new DateTime(2026, 2, 1),
                DueDate = new DateTime(2026, 2, 8),
                DailyRate = 10m, Status = RentalStatus.Overdue
            });

            _giftCardRepo.Add(new GiftCard
            {
                Code = "GIFT-TEST-0004-DDDD",
                OriginalValue = 100m, Balance = 5m, IsActive = true,
                ExpirationDate = new DateTime(2026, 3, 1),
                CreatedDate = new DateTime(2025, 12, 1)
            });

            var report = _service.Analyze();

            for (int i = 1; i < report.Leaks.Count; i++)
            {
                Assert.IsTrue(report.Leaks[i - 1].EstimatedImpact >= report.Leaks[i].EstimatedImpact,
                    "Leaks should be ordered by impact descending");
            }
        }

        [TestMethod]
        public void Analyze_PlaybookPrioritized()
        {
            _rentalRepo.Add(new Rental
            {
                CustomerId = 1, MovieId = 1,
                RentalDate = new DateTime(2026, 2, 1),
                DueDate = new DateTime(2026, 2, 8),
                DailyRate = 5m, Status = RentalStatus.Overdue
            });

            var report = _service.Analyze();

            Assert.IsTrue(report.Playbook.Count > 0);
            for (int i = 0; i < report.Playbook.Count; i++)
            {
                Assert.AreEqual(i + 1, report.Playbook[i].Priority);
            }
        }

        [TestMethod]
        public void Analyze_CategoryBreakdownMatchesLeaks()
        {
            _rentalRepo.Add(new Rental
            {
                CustomerId = 1, MovieId = 1,
                RentalDate = new DateTime(2026, 2, 1),
                DueDate = new DateTime(2026, 2, 8),
                DailyRate = 3m, Status = RentalStatus.Overdue
            });

            var report = _service.Analyze();

            decimal breakdownTotal = report.CategoryBreakdown.Values.Sum();
            Assert.AreEqual(report.TotalLeakage, breakdownTotal, 0.01m);
        }

        [TestMethod]
        public void Analyze_HealthScoreInRange()
        {
            var report = _service.Analyze();
            Assert.IsTrue(report.HealthScore >= 0 && report.HealthScore <= 100);
        }

        // ── AnalyzeCategory ────────────────────────────────────────────────

        [TestMethod]
        public void AnalyzeCategory_ReturnsOnlyRequestedCategory()
        {
            _rentalRepo.Add(new Rental
            {
                CustomerId = 1, MovieId = 1,
                RentalDate = new DateTime(2026, 2, 1),
                DueDate = new DateTime(2026, 2, 8),
                DailyRate = 5m, Status = RentalStatus.Overdue
            });

            var leaks = _service.AnalyzeCategory(LeakCategory.UncollectedLateFees);

            Assert.IsTrue(leaks.All(l => l.Category == LeakCategory.UncollectedLateFees));
        }

        [TestMethod]
        public void AnalyzeCategory_NoMatchingData_ReturnsEmpty()
        {
            var leaks = _service.AnalyzeCategory(LeakCategory.ExpiredGiftCards);
            Assert.AreEqual(0, leaks.Count);
        }

        // ── Severity Classification ────────────────────────────────────────

        [TestMethod]
        public void Analyze_HighImpactLeak_CriticalSeverity()
        {
            // Create enough overdue to hit critical
            for (int i = 0; i < 5; i++)
            {
                _rentalRepo.Add(new Rental
                {
                    CustomerId = i + 1, MovieId = i + 1,
                    RentalDate = new DateTime(2026, 1, 1),
                    DueDate = new DateTime(2026, 1, 8),
                    DailyRate = 10m, Status = RentalStatus.Overdue
                });
            }

            var report = _service.Analyze();
            var lateFeeLeaks = report.Leaks.Where(l => l.Category == LeakCategory.UncollectedLateFees).ToList();

            Assert.IsTrue(lateFeeLeaks.Any(l => l.Severity == LeakSeverity.Critical || l.Severity == LeakSeverity.High));
        }

        // ── Configurable Thresholds ────────────────────────────────────────

        [TestMethod]
        public void Analyze_CustomDormantThreshold_Respected()
        {
            _service.DormantThresholdDays = 30;

            _customerRepo.Add(new Customer
            {
                Name = "Recent Customer", MemberSince = new DateTime(2026, 1, 1),
                MembershipType = MembershipType.Basic
            });
            var customers = _customerRepo.GetAll();

            _rentalRepo.Add(new Rental
            {
                CustomerId = customers[0].Id, MovieId = 1,
                RentalDate = new DateTime(2026, 3, 1),
                DueDate = new DateTime(2026, 3, 8),
                ReturnDate = new DateTime(2026, 3, 7),
                DailyRate = 2.99m, Status = RentalStatus.Returned
            });

            var report = _service.Analyze();
            var leak = report.Leaks.FirstOrDefault(l => l.Category == LeakCategory.DormantCustomers);

            Assert.IsNotNull(leak, "Customer should be dormant with 30-day threshold");
        }

        [TestMethod]
        public void Analyze_MultipleLeakSources_AllDetected()
        {
            // Overdue rental
            _rentalRepo.Add(new Rental
            {
                CustomerId = 1, MovieId = 1,
                RentalDate = new DateTime(2026, 2, 1),
                DueDate = new DateTime(2026, 2, 8),
                DailyRate = 5m, Status = RentalStatus.Overdue
            });

            // Expired gift card
            _giftCardRepo.Add(new GiftCard
            {
                Code = "GIFT-TEST-0005-EEEE",
                OriginalValue = 100m, Balance = 50m, IsActive = true,
                ExpirationDate = new DateTime(2026, 3, 1),
                CreatedDate = new DateTime(2025, 12, 1)
            });

            // Idle movie
            _movieRepo.Add(new Movie { Name = "Forgotten Film", Genre = Genre.Drama });

            // Dormant customer
            _customerRepo.Add(new Customer
            {
                Name = "Ghost Customer", MemberSince = new DateTime(2024, 1, 1),
                MembershipType = MembershipType.Basic
            });

            var report = _service.Analyze();

            Assert.IsTrue(report.Leaks.Count >= 3, "Should detect at least 3 different leak types");
            Assert.IsTrue(report.TotalLeakage > 0);
            Assert.IsTrue(report.Playbook.Count > 0);
            Assert.IsTrue(report.CategoryBreakdown.Count >= 3);
        }

        // ── Trend Detection ────────────────────────────────────────────────

        [TestMethod]
        public void Analyze_TrendIsValid()
        {
            var report = _service.Analyze();
            var validTrends = new[] { "improving", "stable", "worsening" };
            Assert.IsTrue(validTrends.Contains(report.Trend));
        }

        // ── Confidence Bounds ──────────────────────────────────────────────

        [TestMethod]
        public void Analyze_AllLeaksHaveValidConfidence()
        {
            _rentalRepo.Add(new Rental
            {
                CustomerId = 1, MovieId = 1,
                RentalDate = new DateTime(2026, 2, 1),
                DueDate = new DateTime(2026, 2, 8),
                DailyRate = 5m, Status = RentalStatus.Overdue
            });

            var report = _service.Analyze();

            foreach (var leak in report.Leaks)
            {
                Assert.IsTrue(leak.Confidence >= 0.0 && leak.Confidence <= 1.0,
                    string.Format("Confidence {0} out of bounds for {1}", leak.Confidence, leak.Category));
            }
        }

        // ── Recoverable Revenue ────────────────────────────────────────────

        [TestMethod]
        public void Analyze_RecoverableRevenueLessThanOrEqualTotal()
        {
            _rentalRepo.Add(new Rental
            {
                CustomerId = 1, MovieId = 1,
                RentalDate = new DateTime(2026, 2, 1),
                DueDate = new DateTime(2026, 2, 8),
                DailyRate = 10m, Status = RentalStatus.Overdue
            });

            var report = _service.Analyze();

            Assert.IsTrue(report.RecoverableRevenue <= report.TotalLeakage,
                "Recoverable should not exceed total leakage");
        }
    }
}
