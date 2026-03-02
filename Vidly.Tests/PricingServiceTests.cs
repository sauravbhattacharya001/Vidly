using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class PricingServiceTests
    {
        private PricingService _service;

        [TestInitialize]
        public void Setup()
        {
            _service = new PricingService(
                new InMemoryMovieRepository(),
                new InMemoryCustomerRepository(),
                new InMemoryRentalRepository());
        }

        // ── Membership benefits ──────────────────────────────────────

        [TestMethod]
        public void GetBenefits_Basic_NoDiscounts()
        {
            var benefits = PricingService.GetBenefits(MembershipType.Basic);
            Assert.AreEqual(0, benefits.DiscountPercent);
            Assert.AreEqual(0, benefits.GracePeriodDays);
            Assert.AreEqual(2, benefits.MaxConcurrentRentals);
            Assert.AreEqual(0, benefits.FreeRentalsPerMonth);
            Assert.AreEqual(0, benefits.ExtendedRentalDays);
        }

        [TestMethod]
        public void GetBenefits_Silver_10PercentOff()
        {
            var benefits = PricingService.GetBenefits(MembershipType.Silver);
            Assert.AreEqual(10, benefits.DiscountPercent);
            Assert.AreEqual(1, benefits.GracePeriodDays);
            Assert.AreEqual(3, benefits.MaxConcurrentRentals);
            Assert.AreEqual(1, benefits.ExtendedRentalDays);
        }

        [TestMethod]
        public void GetBenefits_Gold_20PercentOff()
        {
            var benefits = PricingService.GetBenefits(MembershipType.Gold);
            Assert.AreEqual(20, benefits.DiscountPercent);
            Assert.AreEqual(2, benefits.GracePeriodDays);
            Assert.AreEqual(5, benefits.MaxConcurrentRentals);
            Assert.AreEqual(1, benefits.FreeRentalsPerMonth);
            Assert.AreEqual(25, benefits.LateFeeDiscount);
        }

        [TestMethod]
        public void GetBenefits_Platinum_30PercentOff()
        {
            var benefits = PricingService.GetBenefits(MembershipType.Platinum);
            Assert.AreEqual(30, benefits.DiscountPercent);
            Assert.AreEqual(3, benefits.GracePeriodDays);
            Assert.AreEqual(10, benefits.MaxConcurrentRentals);
            Assert.AreEqual(3, benefits.FreeRentalsPerMonth);
            Assert.AreEqual(50, benefits.LateFeeDiscount);
        }

        [TestMethod]
        public void GetBenefits_HigherTierAlwaysBetter()
        {
            var basic = PricingService.GetBenefits(MembershipType.Basic);
            var silver = PricingService.GetBenefits(MembershipType.Silver);
            var gold = PricingService.GetBenefits(MembershipType.Gold);
            var platinum = PricingService.GetBenefits(MembershipType.Platinum);

            Assert.IsTrue(silver.DiscountPercent > basic.DiscountPercent);
            Assert.IsTrue(gold.DiscountPercent > silver.DiscountPercent);
            Assert.IsTrue(platinum.DiscountPercent > gold.DiscountPercent);

            Assert.IsTrue(silver.GracePeriodDays >= basic.GracePeriodDays);
            Assert.IsTrue(gold.GracePeriodDays >= silver.GracePeriodDays);
            Assert.IsTrue(platinum.GracePeriodDays >= gold.GracePeriodDays);
        }

        // ── Rental pricing ───────────────────────────────────────────

        [TestMethod]
        public void CalculateRentalPrice_BasicCustomer_NoDiscount()
        {
            // Customer 3 (Bob Wilson) is Basic
            var quote = _service.CalculateRentalPrice(movieId: 1, customerId: 3);

            Assert.AreEqual(PricingService.DefaultDailyRate, quote.BaseDailyRate);
            Assert.AreEqual(0, quote.DiscountPercent);
            Assert.AreEqual(quote.BaseDailyRate, quote.DiscountedDailyRate);
            Assert.AreEqual(PricingService.DefaultRentalDays, quote.RentalDays);
            Assert.IsFalse(quote.FreeRentalApplied);
            Assert.AreEqual(quote.DiscountedDailyRate * quote.RentalDays, quote.FinalTotal);
        }

        [TestMethod]
        public void CalculateRentalPrice_GoldCustomer_20PercentDiscount()
        {
            // Customer 1 (John Smith) is Gold
            var quote = _service.CalculateRentalPrice(movieId: 1, customerId: 1);

            Assert.AreEqual(20, quote.DiscountPercent);
            var expectedRate = PricingService.DefaultDailyRate * 0.80m;
            Assert.AreEqual(expectedRate, quote.DiscountedDailyRate);
            // Gold gets +2 extended days = 9 total
            Assert.AreEqual(PricingService.DefaultRentalDays + 2, quote.RentalDays);
        }

        [TestMethod]
        public void CalculateRentalPrice_CustomRentalDays_OverridesDefault()
        {
            var quote = _service.CalculateRentalPrice(movieId: 1, customerId: 3, rentalDays: 3);
            Assert.AreEqual(3, quote.RentalDays);
        }

        [TestMethod]
        public void CalculateRentalPrice_SavingsCalculated()
        {
            // Gold customer should show savings vs Basic pricing
            var quote = _service.CalculateRentalPrice(movieId: 1, customerId: 1);
            Assert.IsTrue(quote.Savings > 0, "Gold tier should show savings vs base pricing.");
        }

        [TestMethod]
        public void CalculateRentalPrice_DueDateIsCorrect()
        {
            var quote = _service.CalculateRentalPrice(movieId: 1, customerId: 3, rentalDays: 5);
            Assert.AreEqual(DateTime.Today.AddDays(5), quote.DueDate);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CalculateRentalPrice_InvalidMovie_Throws()
        {
            _service.CalculateRentalPrice(movieId: 999, customerId: 1);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CalculateRentalPrice_InvalidCustomer_Throws()
        {
            _service.CalculateRentalPrice(movieId: 1, customerId: 999);
        }

        // ── Late fees ────────────────────────────────────────────────

        [TestMethod]
        public void CalculateLateFee_OnTime_NoFee()
        {
            var rental = new Rental
            {
                Id = 100,
                CustomerId = 3, // Basic - no grace
                DueDate = DateTime.Today.AddDays(1),
                ReturnDate = DateTime.Today,
                Status = RentalStatus.Returned
            };

            var result = _service.CalculateLateFee(rental);
            Assert.AreEqual(0m, result.FinalFee);
            Assert.AreEqual(0, result.EffectiveDaysLate);
        }

        [TestMethod]
        public void CalculateLateFee_OneDayLate_BasicCustomer_ChargesFullRate()
        {
            var rental = new Rental
            {
                Id = 100,
                CustomerId = 3, // Basic - no grace
                DueDate = DateTime.Today.AddDays(-1),
                ReturnDate = DateTime.Today,
                Status = RentalStatus.Returned
            };

            var result = _service.CalculateLateFee(rental);
            Assert.AreEqual(1, result.EffectiveDaysLate);
            Assert.AreEqual(PricingService.LateFeePerDay, result.FinalFee);
        }

        [TestMethod]
        public void CalculateLateFee_WithinGracePeriod_SilverCustomer_NoCharge()
        {
            // Silver has 1-day grace
            var rental = new Rental
            {
                Id = 100,
                CustomerId = 2, // Jane Doe is Silver
                DueDate = DateTime.Today.AddDays(-1),
                ReturnDate = DateTime.Today,
                Status = RentalStatus.Returned
            };

            var result = _service.CalculateLateFee(rental);
            Assert.AreEqual(0m, result.FinalFee);
            Assert.IsTrue(result.WasFeeWaived);
            Assert.AreEqual(1, result.GracePeriodDays);
        }

        [TestMethod]
        public void CalculateLateFee_GoldCustomer_Gets25PercentReduction()
        {
            // Gold: 2-day grace + 25% late fee discount
            var rental = new Rental
            {
                Id = 100,
                CustomerId = 1, // John Smith is Gold
                DueDate = DateTime.Today.AddDays(-5),
                ReturnDate = DateTime.Today,
                Status = RentalStatus.Returned
            };

            var result = _service.CalculateLateFee(rental);
            // 5 days late - 2 grace = 3 effective days
            Assert.AreEqual(3, result.EffectiveDaysLate);
            var baseFee = 3 * PricingService.LateFeePerDay;
            var expected = Math.Round(baseFee * 0.75m, 2); // 25% off
            Assert.AreEqual(expected, result.FinalFee);
        }

        [TestMethod]
        public void CalculateLateFee_PlatinumCustomer_Gets50PercentReduction()
        {
            // Platinum: 3-day grace + 50% late fee discount
            var rental = new Rental
            {
                Id = 100,
                CustomerId = 4, // Alice Johnson is Platinum
                DueDate = DateTime.Today.AddDays(-10),
                ReturnDate = DateTime.Today,
                Status = RentalStatus.Returned
            };

            var result = _service.CalculateLateFee(rental);
            // 10 days late - 3 grace = 7 effective days
            Assert.AreEqual(7, result.EffectiveDaysLate);
            var baseFee = 7 * PricingService.LateFeePerDay;
            var expected = Math.Round(baseFee * 0.50m, 2); // 50% off
            Assert.AreEqual(expected, result.FinalFee);
        }

        [TestMethod]
        public void CalculateLateFee_CappedAtMaximum()
        {
            // Extremely late — should be capped
            var rental = new Rental
            {
                Id = 100,
                CustomerId = 3, // Basic - no grace or discount
                DueDate = DateTime.Today.AddDays(-100),
                ReturnDate = DateTime.Today,
                Status = RentalStatus.Returned
            };

            var result = _service.CalculateLateFee(rental);
            Assert.AreEqual(PricingService.MaxLateFeeCap, result.FinalFee);
        }

        [TestMethod]
        public void CalculateLateFee_HasExplanation()
        {
            var rental = new Rental
            {
                Id = 100,
                CustomerId = 3,
                DueDate = DateTime.Today.AddDays(-3),
                ReturnDate = DateTime.Today,
                Status = RentalStatus.Returned
            };

            var result = _service.CalculateLateFee(rental);
            Assert.IsFalse(string.IsNullOrEmpty(result.Explanation));
            Assert.IsTrue(result.Explanation.Contains("3 day(s) late"));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void CalculateLateFee_NullRental_Throws()
        {
            _service.CalculateLateFee(null);
        }

        // ── Tier comparison ──────────────────────────────────────────

        [TestMethod]
        public void CompareTiers_ReturnsAllTiers()
        {
            var comparisons = _service.CompareTiers(movieId: 1);
            Assert.AreEqual(4, comparisons.Count);
        }

        [TestMethod]
        public void CompareTiers_PlatinumCheapestPerDay()
        {
            var comparisons = _service.CompareTiers(movieId: 1);
            var basic = comparisons.First(c => c.Tier == MembershipType.Basic);
            var platinum = comparisons.First(c => c.Tier == MembershipType.Platinum);
            Assert.IsTrue(platinum.DailyRate < basic.DailyRate);
        }

        [TestMethod]
        public void CompareTiers_HigherTierLowerDailyRate()
        {
            var comparisons = _service.CompareTiers(movieId: 1);
            var ratesByTier = comparisons.OrderBy(c => (int)c.Tier).Select(c => c.DailyRate).ToList();
            for (int i = 1; i < ratesByTier.Count; i++)
            {
                Assert.IsTrue(ratesByTier[i] <= ratesByTier[i - 1],
                    "Higher tier should have equal or lower daily rate.");
            }
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CompareTiers_InvalidMovie_Throws()
        {
            _service.CompareTiers(movieId: 999);
        }

        // ── Billing summary ──────────────────────────────────────────

        [TestMethod]
        public void GetBillingSummary_ReturnsCustomerInfo()
        {
            var summary = _service.GetBillingSummary(customerId: 1);
            Assert.AreEqual(1, summary.CustomerId);
            Assert.AreEqual("John Smith", summary.CustomerName);
            Assert.AreEqual(MembershipType.Gold, summary.MembershipTier);
        }

        [TestMethod]
        public void GetBillingSummary_ShowsRentalSlots()
        {
            var summary = _service.GetBillingSummary(customerId: 1);
            Assert.AreEqual(5, summary.MaxConcurrentRentals); // Gold = 5
        }

        [TestMethod]
        public void GetBillingSummary_IncludesBenefits()
        {
            var summary = _service.GetBillingSummary(customerId: 1);
            Assert.IsNotNull(summary.Benefits);
            Assert.AreEqual(MembershipType.Gold, summary.Benefits.Tier);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetBillingSummary_InvalidCustomer_Throws()
        {
            _service.GetBillingSummary(customerId: 999);
        }

        // ── Constructor validation ───────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new PricingService(
                null,
                new InMemoryCustomerRepository(),
                new InMemoryRentalRepository());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustomerRepo_Throws()
        {
            new PricingService(
                new InMemoryMovieRepository(),
                null,
                new InMemoryRentalRepository());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new PricingService(
                new InMemoryMovieRepository(),
                new InMemoryCustomerRepository(),
                null);
        }

        // ── Constants ────────────────────────────────────────────────

        [TestMethod]
        public void Constants_AreReasonable()
        {
            Assert.IsTrue(PricingService.DefaultDailyRate > 0);
            Assert.IsTrue(PricingService.DefaultRentalDays > 0);
            Assert.IsTrue(PricingService.MaxLateFeeCap > 0);
            Assert.IsTrue(PricingService.LateFeePerDay > 0);
            Assert.IsTrue(PricingService.MaxLateFeeCap > PricingService.LateFeePerDay,
                "Max cap should be greater than one day's fee.");
        }
    }
}
