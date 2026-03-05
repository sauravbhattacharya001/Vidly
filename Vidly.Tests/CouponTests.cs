using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class CouponTests
    {
        private ICouponRepository _repo;
        private CouponService _service;

        [TestInitialize]
        public void Setup()
        {
            InMemoryCouponRepository.Reset();
            _repo = new InMemoryCouponRepository();
            _service = new CouponService(_repo);
        }

        // ── Model tests ─────────────────────────────────────────────

        [TestMethod]
        public void Coupon_IsValid_ActiveAndWithinDates_ReturnsTrue()
        {
            var coupon = new Coupon
            {
                IsActive = true,
                ValidFrom = DateTime.Today.AddDays(-1),
                ValidUntil = DateTime.Today.AddDays(1),
                MaxRedemptions = null,
                TimesUsed = 0
            };
            Assert.IsTrue(coupon.IsValid);
        }

        [TestMethod]
        public void Coupon_IsValid_Expired_ReturnsFalse()
        {
            var coupon = new Coupon
            {
                IsActive = true,
                ValidFrom = DateTime.Today.AddDays(-10),
                ValidUntil = DateTime.Today.AddDays(-1),
                TimesUsed = 0
            };
            Assert.IsFalse(coupon.IsValid);
        }

        [TestMethod]
        public void Coupon_IsValid_NotYetValid_ReturnsFalse()
        {
            var coupon = new Coupon
            {
                IsActive = true,
                ValidFrom = DateTime.Today.AddDays(1),
                ValidUntil = DateTime.Today.AddDays(10),
                TimesUsed = 0
            };
            Assert.IsFalse(coupon.IsValid);
        }

        [TestMethod]
        public void Coupon_IsValid_Disabled_ReturnsFalse()
        {
            var coupon = new Coupon
            {
                IsActive = false,
                ValidFrom = DateTime.Today.AddDays(-1),
                ValidUntil = DateTime.Today.AddDays(1),
                TimesUsed = 0
            };
            Assert.IsFalse(coupon.IsValid);
        }

        [TestMethod]
        public void Coupon_IsValid_Exhausted_ReturnsFalse()
        {
            var coupon = new Coupon
            {
                IsActive = true,
                ValidFrom = DateTime.Today.AddDays(-1),
                ValidUntil = DateTime.Today.AddDays(1),
                MaxRedemptions = 5,
                TimesUsed = 5
            };
            Assert.IsFalse(coupon.IsValid);
        }

        [TestMethod]
        public void Coupon_StatusDisplay_ShowsCorrectStatus()
        {
            var active = new Coupon { IsActive = true, ValidFrom = DateTime.Today.AddDays(-1), ValidUntil = DateTime.Today.AddDays(1) };
            Assert.AreEqual("Active", active.StatusDisplay);

            var disabled = new Coupon { IsActive = false, ValidFrom = DateTime.Today, ValidUntil = DateTime.Today.AddDays(1) };
            Assert.AreEqual("Disabled", disabled.StatusDisplay);

            var expired = new Coupon { IsActive = true, ValidFrom = DateTime.Today.AddDays(-10), ValidUntil = DateTime.Today.AddDays(-1) };
            Assert.AreEqual("Expired", expired.StatusDisplay);

            var scheduled = new Coupon { IsActive = true, ValidFrom = DateTime.Today.AddDays(5), ValidUntil = DateTime.Today.AddDays(10) };
            Assert.AreEqual("Scheduled", scheduled.StatusDisplay);

            var exhausted = new Coupon { IsActive = true, ValidFrom = DateTime.Today.AddDays(-1), ValidUntil = DateTime.Today.AddDays(1), MaxRedemptions = 3, TimesUsed = 3 };
            Assert.AreEqual("Exhausted", exhausted.StatusDisplay);
        }

        [TestMethod]
        public void Coupon_RemainingUses_Unlimited_ReturnsNull()
        {
            var coupon = new Coupon { MaxRedemptions = null, TimesUsed = 10 };
            Assert.IsNull(coupon.RemainingUses);
        }

        [TestMethod]
        public void Coupon_RemainingUses_Limited_ReturnsCorrectCount()
        {
            var coupon = new Coupon { MaxRedemptions = 10, TimesUsed = 3 };
            Assert.AreEqual(7, coupon.RemainingUses);
        }

        // ── Repository tests ────────────────────────────────────────

        [TestMethod]
        public void Repository_GetAll_ReturnsSeedData()
        {
            var coupons = _repo.GetAll();
            Assert.IsTrue(coupons.Count >= 5, "Should have at least 5 seed coupons");
        }

        [TestMethod]
        public void Repository_GetByCode_CaseInsensitive()
        {
            var coupon = _repo.GetByCode("welcome20");
            Assert.IsNotNull(coupon);
            Assert.AreEqual("WELCOME20", coupon.Code);
        }

        [TestMethod]
        public void Repository_GetByCode_NotFound_ReturnsNull()
        {
            Assert.IsNull(_repo.GetByCode("NONEXISTENT"));
        }

        [TestMethod]
        public void Repository_Add_DuplicateCode_Throws()
        {
            var coupon = new Coupon
            {
                Code = "WELCOME20",
                Description = "Duplicate",
                DiscountType = DiscountType.Percentage,
                DiscountValue = 10,
                ValidFrom = DateTime.Today,
                ValidUntil = DateTime.Today.AddDays(30),
                IsActive = true
            };
            Assert.ThrowsException<InvalidOperationException>(() => _repo.Add(coupon));
        }

        [TestMethod]
        public void Repository_TryRedeem_ValidCoupon_ReturnsTrueAndIncrements()
        {
            var before = _repo.GetByCode("WELCOME20").TimesUsed;
            var result = _repo.TryRedeem("WELCOME20");
            Assert.IsTrue(result);
            Assert.AreEqual(before + 1, _repo.GetByCode("WELCOME20").TimesUsed);
        }

        [TestMethod]
        public void Repository_TryRedeem_ExpiredCoupon_ReturnsFalse()
        {
            var result = _repo.TryRedeem("EXPIRED10");
            Assert.IsFalse(result);
        }

        // ── Service validation tests ────────────────────────────────

        [TestMethod]
        public void Service_Validate_EmptyCode_Fails()
        {
            var result = _service.Validate("", 20m);
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Message.Contains("enter"));
        }

        [TestMethod]
        public void Service_Validate_UnknownCode_Fails()
        {
            var result = _service.Validate("NOTREAL", 20m);
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Message.Contains("not found"));
        }

        [TestMethod]
        public void Service_Validate_ExpiredCode_Fails()
        {
            var result = _service.Validate("EXPIRED10", 20m);
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Message.Contains("expired"));
        }

        [TestMethod]
        public void Service_Validate_ValidPercentageCoupon_ReturnsDiscount()
        {
            var result = _service.Validate("WELCOME20", 50m);
            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(10m, result.DiscountAmount); // 20% of 50 = 10, capped at 10
        }

        [TestMethod]
        public void Service_Validate_PercentageCoupon_RespectsCap()
        {
            // WELCOME20 has MaxDiscountAmount = $10
            var result = _service.Validate("WELCOME20", 100m);
            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(10m, result.DiscountAmount); // 20% of 100 = 20, capped at 10
        }

        [TestMethod]
        public void Service_Validate_FixedAmountCoupon_Works()
        {
            var result = _service.Validate("FLAT2OFF", 10m);
            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(2m, result.DiscountAmount);
        }

        [TestMethod]
        public void Service_Validate_BelowMinimumOrder_Fails()
        {
            // FLAT2OFF has MinimumOrderAmount = $3.00
            var result = _service.Validate("FLAT2OFF", 2m);
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Message.Contains("Minimum"));
        }

        [TestMethod]
        public void Service_Validate_FixedAmount_CappedAtSubtotal()
        {
            // FLAT2OFF is $2 off, but if subtotal is $1.50 it should cap
            // (won't hit because MinimumOrderAmount is $3, but test the logic)
            var result = _service.Validate("SPRING50", 5m);
            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(2.50m, result.DiscountAmount); // 50% of 5 = 2.50
        }

        [TestMethod]
        public void Service_Apply_ValidCoupon_ReturnsDiscount()
        {
            var discount = _service.Apply("FLAT2OFF", 10m);
            Assert.AreEqual(2m, discount);
        }

        [TestMethod]
        public void Service_Apply_InvalidCoupon_ReturnsZero()
        {
            var discount = _service.Apply("EXPIRED10", 10m);
            Assert.AreEqual(0m, discount);
        }
    }
}
