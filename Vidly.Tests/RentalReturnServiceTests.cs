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
    public class RentalReturnServiceTests
    {
        private IRentalRepository _rentalRepo;
        private IMovieRepository _movieRepo;
        private ICustomerRepository _customerRepo;
        private RentalReturnService _service;

        [TestInitialize]
        public void Setup()
        {
            InMemoryRentalRepository.Reset();
            InMemoryMovieRepository.Reset();
            InMemoryCustomerRepository.Reset();

            _rentalRepo = new InMemoryRentalRepository();
            _movieRepo = new InMemoryMovieRepository();
            _customerRepo = new InMemoryCustomerRepository();

            _service = new RentalReturnService(
                _rentalRepo, _movieRepo, _customerRepo);
        }

        // Helper to create a rental that's ready to return
        private Rental CreateActiveRental(
            int customerId = 1, int movieId = 1,
            DateTime? rentalDate = null, int rentalDays = 7,
            decimal dailyRate = 3.99m)
        {
            var rDate = rentalDate ?? DateTime.Today.AddDays(-rentalDays);
            var rental = new Rental
            {
                CustomerId = customerId,
                MovieId = movieId,
                RentalDate = rDate,
                DueDate = rDate.AddDays(rentalDays),
                DailyRate = dailyRate,
                Status = RentalStatus.Active
            };
            _rentalRepo.Add(rental);
            return rental;
        }

        // ── Constructor validation ───────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new RentalReturnService(null, _movieRepo, _customerRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new RentalReturnService(_rentalRepo, null, _customerRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustomerRepo_Throws()
        {
            new RentalReturnService(_rentalRepo, _movieRepo, null);
        }

        // ── ProcessReturn: basic flow ────────────────────────────────

        [TestMethod]
        public void ProcessReturn_OnTime_ReturnsReceiptWithNoLateFee()
        {
            var rental = CreateActiveRental(rentalDays: 7);
            var returnDate = rental.DueDate; // exactly on time

            var receipt = _service.ProcessReturn(
                rental.Id, ReturnCondition.Good, returnDate);

            Assert.AreEqual(rental.Id, receipt.RentalId);
            Assert.IsTrue(receipt.ReturnedOnTime);
            Assert.AreEqual(0m, receipt.LateFee);
            Assert.AreEqual(0m, receipt.DamageCharge);
            Assert.IsTrue(receipt.LoyaltyPointsEarned > 0);
        }

        [TestMethod]
        public void ProcessReturn_MarksRentalAsReturned()
        {
            var rental = CreateActiveRental();
            _service.ProcessReturn(rental.Id);

            var updated = _rentalRepo.GetById(rental.Id);
            Assert.AreEqual(RentalStatus.Returned, updated.Status);
            Assert.IsNotNull(updated.ReturnDate);
        }

        [TestMethod]
        public void ProcessReturn_EarlyReturn_NoLateFee()
        {
            var rental = CreateActiveRental(rentalDays: 7);
            var earlyReturn = rental.DueDate.AddDays(-2);

            var receipt = _service.ProcessReturn(
                rental.Id, ReturnCondition.Good, earlyReturn);

            Assert.IsTrue(receipt.ReturnedOnTime);
            Assert.AreEqual(0m, receipt.LateFee);
            Assert.AreEqual(0, receipt.DaysOverdue);
        }

        // ── ProcessReturn: validation ────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ProcessReturn_NonExistentRental_Throws()
        {
            _service.ProcessReturn(99999);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ProcessReturn_AlreadyReturned_Throws()
        {
            var rental = CreateActiveRental();
            _service.ProcessReturn(rental.Id);
            _service.ProcessReturn(rental.Id); // second time
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ProcessReturn_ReturnBeforeRental_Throws()
        {
            var rental = CreateActiveRental(
                rentalDate: DateTime.Today);

            _service.ProcessReturn(
                rental.Id, ReturnCondition.Good,
                DateTime.Today.AddDays(-1));
        }

        // ── Late fees ────────────────────────────────────────────────

        [TestMethod]
        public void ProcessReturn_LateFee_BasicTier_Charged()
        {
            var rental = CreateActiveRental(rentalDays: 7);
            // Return 5 days late (1 day grace for Basic → 4 chargeable)
            var lateReturn = rental.DueDate.AddDays(5);

            var receipt = _service.ProcessReturn(
                rental.Id, ReturnCondition.Good, lateReturn);

            Assert.IsFalse(receipt.ReturnedOnTime);
            Assert.AreEqual(5, receipt.DaysOverdue);
            Assert.IsTrue(receipt.LateFee > 0);
        }

        [TestMethod]
        public void ProcessReturn_LateFee_WithinGracePeriod_NoFee()
        {
            var rental = CreateActiveRental(rentalDays: 7);
            // Basic grace = 1 day, so exactly 1 day late = within grace
            var lateReturn = rental.DueDate.AddDays(1);

            var receipt = _service.ProcessReturn(
                rental.Id, ReturnCondition.Good, lateReturn);

            Assert.AreEqual(1, receipt.DaysOverdue);
            Assert.AreEqual(0m, receipt.LateFee);
        }

        [TestMethod]
        public void CalculateLateFee_NotLate_ZeroFee()
        {
            var result = _service.CalculateLateFee(
                DateTime.Today, DateTime.Today, 3.99m, MembershipType.Basic);

            Assert.AreEqual(0, result.DaysOverdue);
            Assert.AreEqual(0m, result.LateFee);
        }

        [TestMethod]
        public void CalculateLateFee_BasicTier_CorrectCharge()
        {
            // 5 days overdue, Basic grace = 1 → 4 chargeable × $1.50
            var result = _service.CalculateLateFee(
                DateTime.Today.AddDays(-5), DateTime.Today,
                3.99m, MembershipType.Basic);

            Assert.AreEqual(5, result.DaysOverdue);
            Assert.AreEqual(1, result.GracePeriodDays);
            Assert.AreEqual(1, result.WaivedDays);
            Assert.AreEqual(6.00m, result.LateFee); // 4 × 1.50
        }

        [TestMethod]
        public void CalculateLateFee_GoldTier_LongerGraceAndDiscount()
        {
            // 5 days overdue, Gold grace = 3 → 2 chargeable × $1.50 × 0.75
            var result = _service.CalculateLateFee(
                DateTime.Today.AddDays(-5), DateTime.Today,
                3.99m, MembershipType.Gold);

            Assert.AreEqual(5, result.DaysOverdue);
            Assert.AreEqual(3, result.GracePeriodDays);
            Assert.AreEqual(3, result.WaivedDays);
            Assert.AreEqual(2.25m, result.LateFee); // 2 × 1.50 × 0.75
        }

        [TestMethod]
        public void CalculateLateFee_PlatinumTier_MostGenerous()
        {
            // 5 days overdue, Platinum grace = 5 → 0 chargeable
            var result = _service.CalculateLateFee(
                DateTime.Today.AddDays(-5), DateTime.Today,
                3.99m, MembershipType.Platinum);

            Assert.AreEqual(5, result.DaysOverdue);
            Assert.AreEqual(5, result.GracePeriodDays);
            Assert.AreEqual(0m, result.LateFee);
        }

        [TestMethod]
        public void CalculateLateFee_Cap_Applied()
        {
            // 30 days overdue, Basic → 29 chargeable × $1.50 = $43.50 → capped at $25
            var result = _service.CalculateLateFee(
                DateTime.Today.AddDays(-30), DateTime.Today,
                3.99m, MembershipType.Basic);

            Assert.AreEqual(RentalReturnService.MaxLateFeeCap, result.LateFee);
            Assert.IsTrue(result.FeeCapped);
        }

        // ── Grace period ─────────────────────────────────────────────

        [TestMethod]
        public void GetGracePeriod_Basic_1Day()
        {
            Assert.AreEqual(1, RentalReturnService.GetGracePeriod(MembershipType.Basic));
        }

        [TestMethod]
        public void GetGracePeriod_Silver_2Days()
        {
            Assert.AreEqual(2, RentalReturnService.GetGracePeriod(MembershipType.Silver));
        }

        [TestMethod]
        public void GetGracePeriod_Gold_3Days()
        {
            Assert.AreEqual(3, RentalReturnService.GetGracePeriod(MembershipType.Gold));
        }

        [TestMethod]
        public void GetGracePeriod_Platinum_5Days()
        {
            Assert.AreEqual(5, RentalReturnService.GetGracePeriod(MembershipType.Platinum));
        }

        // ── Tier discounts ───────────────────────────────────────────

        [TestMethod]
        public void GetTierLateDiscount_Basic_Zero()
        {
            Assert.AreEqual(0m, RentalReturnService.GetTierLateDiscount(MembershipType.Basic));
        }

        [TestMethod]
        public void GetTierLateDiscount_Platinum_50Percent()
        {
            Assert.AreEqual(0.50m, RentalReturnService.GetTierLateDiscount(MembershipType.Platinum));
        }

        // ── Damage charges ───────────────────────────────────────────

        [TestMethod]
        public void GetDamageCharge_Good_Zero()
        {
            Assert.AreEqual(0m, RentalReturnService.GetDamageCharge(ReturnCondition.Good));
        }

        [TestMethod]
        public void GetDamageCharge_Fair_Zero()
        {
            Assert.AreEqual(0m, RentalReturnService.GetDamageCharge(ReturnCondition.Fair));
        }

        [TestMethod]
        public void GetDamageCharge_MinorDamage_Charged()
        {
            Assert.AreEqual(RentalReturnService.MinorDamageCharge,
                RentalReturnService.GetDamageCharge(ReturnCondition.MinorDamage));
        }

        [TestMethod]
        public void GetDamageCharge_ModerateDamage_Charged()
        {
            Assert.AreEqual(RentalReturnService.ModerateDamageCharge,
                RentalReturnService.GetDamageCharge(ReturnCondition.ModerateDamage));
        }

        [TestMethod]
        public void GetDamageCharge_SevereDamage_FullReplacement()
        {
            Assert.AreEqual(RentalReturnService.SevereDamageCharge,
                RentalReturnService.GetDamageCharge(ReturnCondition.SevereDamage));
        }

        [TestMethod]
        public void ProcessReturn_WithDamage_AddedToTotal()
        {
            var rental = CreateActiveRental(rentalDays: 7);
            var returnDate = rental.DueDate;

            var receipt = _service.ProcessReturn(
                rental.Id, ReturnCondition.MinorDamage, returnDate);

            Assert.AreEqual(RentalReturnService.MinorDamageCharge, receipt.DamageCharge);
            Assert.IsTrue(receipt.TotalCost > receipt.BaseCost);
        }

        // ── Loyalty points ───────────────────────────────────────────

        [TestMethod]
        public void CalculateLoyaltyPoints_OnTime_GoodCondition_MaxPoints()
        {
            var lateFee = new ReturnLateFeeBreakdown { DaysOverdue = 0 };
            var points = RentalReturnService.CalculateLoyaltyPoints(
                lateFee, ReturnCondition.Good, MembershipType.Basic);

            Assert.AreEqual(
                RentalReturnService.OnTimeReturnBonus +
                RentalReturnService.PerfectConditionBonus,
                points);
        }

        [TestMethod]
        public void CalculateLoyaltyPoints_Late_NoBonuses()
        {
            var lateFee = new ReturnLateFeeBreakdown { DaysOverdue = 5 };
            var points = RentalReturnService.CalculateLoyaltyPoints(
                lateFee, ReturnCondition.Fair, MembershipType.Basic);

            Assert.AreEqual(0, points);
        }

        [TestMethod]
        public void CalculateLoyaltyPoints_VeryLate_Penalty()
        {
            var lateFee = new ReturnLateFeeBreakdown { DaysOverdue = 20 };
            var points = RentalReturnService.CalculateLoyaltyPoints(
                lateFee, ReturnCondition.Fair, MembershipType.Basic);

            Assert.AreEqual(-RentalReturnService.VeryLateReturnPenalty, points);
        }

        [TestMethod]
        public void CalculateLoyaltyPoints_HigherTier_Multiplied()
        {
            var lateFee = new ReturnLateFeeBreakdown { DaysOverdue = 0 };

            var basic = RentalReturnService.CalculateLoyaltyPoints(
                lateFee, ReturnCondition.Good, MembershipType.Basic);
            var gold = RentalReturnService.CalculateLoyaltyPoints(
                lateFee, ReturnCondition.Good, MembershipType.Gold);
            var plat = RentalReturnService.CalculateLoyaltyPoints(
                lateFee, ReturnCondition.Good, MembershipType.Platinum);

            Assert.IsTrue(gold > basic);
            Assert.IsTrue(plat > gold);
        }

        // ── Tier point multipliers ───────────────────────────────────

        [TestMethod]
        public void GetTierPointMultiplier_Increases()
        {
            Assert.AreEqual(1.0m, RentalReturnService.GetTierPointMultiplier(MembershipType.Basic));
            Assert.AreEqual(1.25m, RentalReturnService.GetTierPointMultiplier(MembershipType.Silver));
            Assert.AreEqual(1.50m, RentalReturnService.GetTierPointMultiplier(MembershipType.Gold));
            Assert.AreEqual(2.0m, RentalReturnService.GetTierPointMultiplier(MembershipType.Platinum));
        }

        // ── Batch returns ────────────────────────────────────────────

        [TestMethod]
        public void BatchReturn_AllSuccess()
        {
            var r1 = CreateActiveRental(customerId: 1, movieId: 1);
            var r2 = CreateActiveRental(customerId: 1, movieId: 2);

            var result = _service.ProcessBatchReturn(
                new[] { r1.Id, r2.Id });

            Assert.AreEqual(2, result.TotalReturned);
            Assert.AreEqual(0, result.TotalFailed);
            Assert.AreEqual(2, result.Receipts.Count);
            Assert.IsTrue(result.GrandTotal > 0);
        }

        [TestMethod]
        public void BatchReturn_PartialFailure_ContinuesProcessing()
        {
            var r1 = CreateActiveRental(customerId: 1, movieId: 1);

            var result = _service.ProcessBatchReturn(
                new[] { r1.Id, 99999 });

            Assert.AreEqual(1, result.TotalReturned);
            Assert.AreEqual(1, result.TotalFailed);
            Assert.IsTrue(result.Errors.ContainsKey(99999));
        }

        [TestMethod]
        public void BatchReturn_Totals_SumCorrectly()
        {
            var r1 = CreateActiveRental(customerId: 1, movieId: 1, dailyRate: 3.99m);
            var r2 = CreateActiveRental(customerId: 1, movieId: 2, dailyRate: 5.99m);

            var result = _service.ProcessBatchReturn(
                new[] { r1.Id, r2.Id });

            Assert.AreEqual(
                result.Receipts.Sum(r => r.BaseCost),
                result.TotalBaseCost);
            Assert.AreEqual(
                result.Receipts.Sum(r => r.TotalCost),
                result.GrandTotal);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void BatchReturn_NullIds_Throws()
        {
            _service.ProcessBatchReturn(null);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void BatchReturn_EmptyIds_Throws()
        {
            _service.ProcessBatchReturn(new List<int>());
        }

        // ── Overdue management ───────────────────────────────────────

        [TestMethod]
        public void GetOverdueRentals_ReturnsOverdueOnly()
        {
            // Seed data includes some overdue rentals
            var overdueList = _service.GetOverdueRentals();

            foreach (var info in overdueList)
            {
                Assert.IsTrue(info.DaysOverdue > 0);
                Assert.IsTrue(info.ProjectedLateFee >= 0);
            }
        }

        [TestMethod]
        public void GetOverdueRentals_OrderedByDaysOverdue()
        {
            var overdueList = _service.GetOverdueRentals();

            if (overdueList.Count > 1)
            {
                for (int i = 1; i < overdueList.Count; i++)
                {
                    Assert.IsTrue(
                        overdueList[i - 1].DaysOverdue >= overdueList[i].DaysOverdue);
                }
            }
        }

        [TestMethod]
        public void GetOverdueRentals_ActionEscalates()
        {
            // Verify that more overdue → more severe action
            var info3 = new OverdueRentalInfo { DaysOverdue = 2 };
            var info7 = new OverdueRentalInfo { DaysOverdue = 6 };
            var info14 = new OverdueRentalInfo { DaysOverdue = 10 };
            var info30 = new OverdueRentalInfo { DaysOverdue = 20 };

            // We test the action logic indirectly via the actual service
            var overdueList = _service.GetOverdueRentals();

            foreach (var item in overdueList)
            {
                if (item.DaysOverdue <= 3)
                    Assert.AreEqual(OverdueAction.ReminderNotice, item.RecommendedAction);
                else if (item.DaysOverdue <= 7)
                    Assert.AreEqual(OverdueAction.LateFeeWarning, item.RecommendedAction);
                else if (item.DaysOverdue <= 14)
                    Assert.AreEqual(OverdueAction.FinalNotice, item.RecommendedAction);
                else
                    Assert.AreEqual(OverdueAction.AccountHold, item.RecommendedAction);
            }
        }

        // ── Overdue summary ──────────────────────────────────────────

        [TestMethod]
        public void GetOverdueSummary_EmptyWhenNoneOverdue()
        {
            // Return all overdue rentals first
            var overdueRentals = _rentalRepo.GetOverdue();
            foreach (var r in overdueRentals)
            {
                try { _service.ProcessReturn(r.Id); } catch { }
            }

            var summary = _service.GetOverdueSummary();
            Assert.AreEqual(0, summary.TotalOverdue);
            Assert.AreEqual(0m, summary.TotalProjectedFees);
        }

        [TestMethod]
        public void GetOverdueSummary_BreakdownsNotNull()
        {
            var summary = _service.GetOverdueSummary();
            Assert.IsNotNull(summary.ByAction);
            Assert.IsNotNull(summary.ByTier);
        }

        // ── Late-fee estimation ──────────────────────────────────────

        [TestMethod]
        public void EstimateCurrentLateFee_ActiveRental_ReturnsEstimate()
        {
            var rental = CreateActiveRental(
                rentalDays: 7,
                rentalDate: DateTime.Today.AddDays(-10)); // 3 days overdue

            var estimate = _service.EstimateCurrentLateFee(rental.Id);

            Assert.AreEqual(rental.Id, estimate.RentalId);
            Assert.IsTrue(estimate.DaysOverdue >= 0);
            Assert.IsFalse(string.IsNullOrEmpty(estimate.Message));
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void EstimateCurrentLateFee_NotFound_Throws()
        {
            _service.EstimateCurrentLateFee(99999);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void EstimateCurrentLateFee_AlreadyReturned_Throws()
        {
            var rental = CreateActiveRental();
            _service.ProcessReturn(rental.Id);
            _service.EstimateCurrentLateFee(rental.Id);
        }

        [TestMethod]
        public void EstimateCurrentLateFee_NotYetDue_FriendlyMessage()
        {
            var rental = CreateActiveRental(
                rentalDays: 30,
                rentalDate: DateTime.Today); // due in 30 days

            var estimate = _service.EstimateCurrentLateFee(rental.Id);

            Assert.AreEqual(0, estimate.DaysOverdue);
            Assert.AreEqual(0m, estimate.EstimatedFee);
            Assert.IsTrue(estimate.Message.Contains("No late fee"));
        }

        // ── Customer return profile ──────────────────────────────────

        [TestMethod]
        public void GetCustomerReturnProfile_ValidCustomer()
        {
            var profile = _service.GetCustomerReturnProfile(1);

            Assert.AreEqual(1, profile.CustomerId);
            Assert.IsFalse(string.IsNullOrEmpty(profile.CustomerName));
            Assert.IsTrue(profile.OnTimeRate >= 0 && profile.OnTimeRate <= 100);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void GetCustomerReturnProfile_NotFound_Throws()
        {
            _service.GetCustomerReturnProfile(99999);
        }

        [TestMethod]
        public void GetCustomerReturnProfile_Reliability_Rated()
        {
            var profile = _service.GetCustomerReturnProfile(1);

            Assert.IsTrue(Enum.IsDefined(typeof(CustomerReliability),
                profile.Reliability));
        }

        // ── Receipt integrity ────────────────────────────────────────

        [TestMethod]
        public void ProcessReturn_Receipt_TotalCostConsistent()
        {
            var rental = CreateActiveRental(rentalDays: 7);
            var lateReturn = rental.DueDate.AddDays(3);

            var receipt = _service.ProcessReturn(
                rental.Id, ReturnCondition.MinorDamage, lateReturn);

            Assert.AreEqual(
                receipt.BaseCost + receipt.LateFee + receipt.DamageCharge,
                receipt.TotalCost);
        }

        [TestMethod]
        public void ProcessReturn_Receipt_DaysRentedPositive()
        {
            var rental = CreateActiveRental(rentalDays: 7);
            var receipt = _service.ProcessReturn(
                rental.Id, ReturnCondition.Good, rental.DueDate);

            Assert.IsTrue(receipt.DaysRented > 0);
        }

        [TestMethod]
        public void ProcessReturn_Receipt_IncludesCustomerInfo()
        {
            var rental = CreateActiveRental(customerId: 1);
            var receipt = _service.ProcessReturn(rental.Id);

            Assert.AreEqual(1, receipt.CustomerId);
            Assert.IsFalse(string.IsNullOrEmpty(receipt.CustomerName));
        }

        // ── Edge cases ───────────────────────────────────────────────

        [TestMethod]
        public void ProcessReturn_SameDay_OneDayMinimum()
        {
            var rental = CreateActiveRental(
                rentalDays: 7,
                rentalDate: DateTime.Today);

            var receipt = _service.ProcessReturn(
                rental.Id, ReturnCondition.Good, DateTime.Today);

            Assert.IsTrue(receipt.DaysRented >= 1);
        }

        [TestMethod]
        public void CalculateLateFee_SilverTier_Discount()
        {
            // 4 days overdue, Silver grace = 2 → 2 chargeable × $1.50 × 0.90
            var result = _service.CalculateLateFee(
                DateTime.Today.AddDays(-4), DateTime.Today,
                3.99m, MembershipType.Silver);

            Assert.AreEqual(4, result.DaysOverdue);
            Assert.AreEqual(2, result.GracePeriodDays);
            Assert.AreEqual(2.70m, result.LateFee); // 2 × 1.50 × 0.90
        }

        [TestMethod]
        public void ProcessReturn_DefaultCondition_Good()
        {
            var rental = CreateActiveRental();
            var receipt = _service.ProcessReturn(rental.Id);

            Assert.AreEqual(ReturnCondition.Good, receipt.Condition);
            Assert.AreEqual(0m, receipt.DamageCharge);
        }

        [TestMethod]
        public void ProcessReturn_DefaultReturnDate_Today()
        {
            var rental = CreateActiveRental(rentalDays: 3,
                rentalDate: DateTime.Today.AddDays(-3));
            var receipt = _service.ProcessReturn(rental.Id);

            Assert.AreEqual(DateTime.Today, receipt.ReturnDate);
        }
    }
}

