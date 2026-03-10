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
    public class CouponServiceTests
    {
        #region Test Repository

        private class StubCouponRepository : ICouponRepository
        {
            private readonly Dictionary<string, Coupon> _coupons =
                new Dictionary<string, Coupon>(StringComparer.OrdinalIgnoreCase);
            private int _nextId = 1;

            public void AddCoupon(Coupon coupon)
            {
                coupon.Id = _nextId++;
                _coupons[coupon.Code] = coupon;
            }

            public IReadOnlyList<Coupon> GetAll() => _coupons.Values.ToList();
            public Coupon GetById(int id) => _coupons.Values.FirstOrDefault(c => c.Id == id);
            public Coupon GetByCode(string code) =>
                code != null && _coupons.TryGetValue(code.Trim(), out var c) ? c : null;
            public void Add(Coupon coupon) => AddCoupon(coupon);
            public void Update(Coupon coupon) { }
            public void Remove(int id) { }

            public bool TryRedeem(string code)
            {
                if (string.IsNullOrWhiteSpace(code)) return false;
                var coupon = GetByCode(code);
                if (coupon == null || !coupon.IsValid) return false;
                coupon.TimesUsed++;
                return true;
            }
        }

        #endregion

        private StubCouponRepository _repo;
        private CouponService _service;

        [TestInitialize]
        public void Setup()
        {
            _repo = new StubCouponRepository();
            _service = new CouponService(_repo);
        }

        private Coupon MakeValidPercentCoupon(string code = "SAVE20", decimal value = 20)
        {
            return new Coupon
            {
                Code = code,
                Description = $"{value}% off",
                DiscountType = DiscountType.Percentage,
                DiscountValue = value,
                MinimumOrderAmount = 0,
                MaxDiscountAmount = null,
                ValidFrom = DateTime.Today.AddDays(-1),
                ValidUntil = DateTime.Today.AddDays(30),
                MaxRedemptions = null,
                TimesUsed = 0,
                IsActive = true
            };
        }

        private Coupon MakeValidFixedCoupon(string code = "FLAT5", decimal value = 5)
        {
            return new Coupon
            {
                Code = code,
                Description = $"${value} off",
                DiscountType = DiscountType.FixedAmount,
                DiscountValue = value,
                MinimumOrderAmount = 0,
                MaxDiscountAmount = null,
                ValidFrom = DateTime.Today.AddDays(-1),
                ValidUntil = DateTime.Today.AddDays(30),
                MaxRedemptions = null,
                TimesUsed = 0,
                IsActive = true
            };
        }

        // ── Constructor ──────────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRepository_Throws()
        {
            new CouponService(null);
        }

        // ── Validate: invalid inputs ─────────────────────────────

        [TestMethod]
        public void Validate_NullCode_ReturnsInvalid()
        {
            var result = _service.Validate(null, 10m);
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Message.Contains("enter"));
        }

        [TestMethod]
        public void Validate_EmptyCode_ReturnsInvalid()
        {
            var result = _service.Validate("", 10m);
            Assert.IsFalse(result.IsValid);
        }

        [TestMethod]
        public void Validate_WhitespaceCode_ReturnsInvalid()
        {
            var result = _service.Validate("   ", 10m);
            Assert.IsFalse(result.IsValid);
        }

        [TestMethod]
        public void Validate_NonExistentCode_ReturnsNotFound()
        {
            var result = _service.Validate("NONEXISTENT", 10m);
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Message.Contains("not found"));
        }

        // ── Validate: inactive coupon ────────────────────────────

        [TestMethod]
        public void Validate_InactiveCoupon_ReturnsDisabled()
        {
            var coupon = MakeValidPercentCoupon("INACTIVE");
            coupon.IsActive = false;
            _repo.AddCoupon(coupon);

            var result = _service.Validate("INACTIVE", 10m);
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Message.Contains("disabled"));
        }

        // ── Validate: date checks ────────────────────────────────

        [TestMethod]
        public void Validate_FutureCoupon_ReturnsNotYetValid()
        {
            var coupon = MakeValidPercentCoupon("FUTURE");
            coupon.ValidFrom = DateTime.Today.AddDays(10);
            _repo.AddCoupon(coupon);

            var result = _service.Validate("FUTURE", 10m);
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Message.Contains("not valid until"));
        }

        [TestMethod]
        public void Validate_ExpiredCoupon_ReturnsExpired()
        {
            var coupon = MakeValidPercentCoupon("EXPIRED");
            coupon.ValidFrom = DateTime.Today.AddDays(-60);
            coupon.ValidUntil = DateTime.Today.AddDays(-1);
            _repo.AddCoupon(coupon);

            var result = _service.Validate("EXPIRED", 10m);
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Message.Contains("expired"));
        }

        // ── Validate: redemption limit ───────────────────────────

        [TestMethod]
        public void Validate_ExhaustedCoupon_ReturnsLimitReached()
        {
            var coupon = MakeValidPercentCoupon("MAXED");
            coupon.MaxRedemptions = 5;
            coupon.TimesUsed = 5;
            _repo.AddCoupon(coupon);

            var result = _service.Validate("MAXED", 10m);
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Message.Contains("redemption limit"));
        }

        // ── Validate: minimum order ──────────────────────────────

        [TestMethod]
        public void Validate_BelowMinimumOrder_ReturnsMinimumRequired()
        {
            var coupon = MakeValidPercentCoupon("MINORD");
            coupon.MinimumOrderAmount = 20m;
            _repo.AddCoupon(coupon);

            var result = _service.Validate("MINORD", 10m);
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Message.Contains("Minimum order"));
        }

        // ── Validate: successful percentage ──────────────────────

        [TestMethod]
        public void Validate_ValidPercentageCoupon_ReturnsSuccess()
        {
            var coupon = MakeValidPercentCoupon("PCT20", 20);
            _repo.AddCoupon(coupon);

            var result = _service.Validate("PCT20", 50m);
            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(10.00m, result.DiscountAmount); // 20% of $50
            Assert.AreEqual("PCT20", result.CouponCode);
        }

        [TestMethod]
        public void Validate_PercentageWithMaxDiscount_CapsDiscount()
        {
            var coupon = MakeValidPercentCoupon("CAPPED", 50);
            coupon.MaxDiscountAmount = 8.00m;
            _repo.AddCoupon(coupon);

            var result = _service.Validate("CAPPED", 50m);
            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(8.00m, result.DiscountAmount); // 50% of $50 = $25, capped at $8
        }

        // ── Validate: successful fixed amount ────────────────────

        [TestMethod]
        public void Validate_ValidFixedCoupon_ReturnsSuccess()
        {
            var coupon = MakeValidFixedCoupon("FIX5", 5);
            _repo.AddCoupon(coupon);

            var result = _service.Validate("FIX5", 20m);
            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(5.00m, result.DiscountAmount);
        }

        [TestMethod]
        public void Validate_FixedCoupon_NeverExceedsSubtotal()
        {
            var coupon = MakeValidFixedCoupon("BIG", 50);
            _repo.AddCoupon(coupon);

            var result = _service.Validate("BIG", 10m);
            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(10.00m, result.DiscountAmount); // capped at subtotal
        }

        // ── Validate: case insensitivity ─────────────────────────

        [TestMethod]
        public void Validate_CaseInsensitive_Works()
        {
            _repo.AddCoupon(MakeValidPercentCoupon("MYCODE", 10));

            var result = _service.Validate("mycode", 100m);
            Assert.IsTrue(result.IsValid);
        }

        // ── Apply ────────────────────────────────────────────────

        [TestMethod]
        public void Apply_ValidCoupon_ReturnsDiscount()
        {
            _repo.AddCoupon(MakeValidFixedCoupon("APPLY5", 5));
            var discount = _service.Apply("APPLY5", 20m);
            Assert.AreEqual(5.00m, discount);
        }

        [TestMethod]
        public void Apply_ValidCoupon_IncrementsUsage()
        {
            var coupon = MakeValidFixedCoupon("USE1", 3);
            _repo.AddCoupon(coupon);
            _service.Apply("USE1", 20m);
            Assert.AreEqual(1, coupon.TimesUsed);
        }

        [TestMethod]
        public void Apply_InvalidCoupon_ReturnsZero()
        {
            var discount = _service.Apply("NONEXISTENT", 20m);
            Assert.AreEqual(0m, discount);
        }

        [TestMethod]
        public void Apply_ExpiredCoupon_ReturnsZero()
        {
            var coupon = MakeValidFixedCoupon("EXP", 5);
            coupon.ValidUntil = DateTime.Today.AddDays(-1);
            _repo.AddCoupon(coupon);

            var discount = _service.Apply("EXP", 20m);
            Assert.AreEqual(0m, discount);
        }

        [TestMethod]
        public void Apply_ExhaustedCoupon_ReturnsZero()
        {
            var coupon = MakeValidFixedCoupon("DONE", 5);
            coupon.MaxRedemptions = 1;
            coupon.TimesUsed = 1;
            _repo.AddCoupon(coupon);

            var discount = _service.Apply("DONE", 20m);
            Assert.AreEqual(0m, discount);
        }

        // ── CouponValidationResult message format ────────────────

        [TestMethod]
        public void Validate_PercentageSuccess_MessageContainsPercent()
        {
            _repo.AddCoupon(MakeValidPercentCoupon("PMSG", 15));
            var result = _service.Validate("PMSG", 100m);
            Assert.IsTrue(result.Message.Contains("15%"));
        }

        [TestMethod]
        public void Validate_FixedSuccess_MessageContainsDollar()
        {
            _repo.AddCoupon(MakeValidFixedCoupon("FMSG", 7));
            var result = _service.Validate("FMSG", 100m);
            Assert.IsTrue(result.Message.Contains("$"));
        }

        // ── Coupon model properties ──────────────────────────────

        [TestMethod]
        public void Coupon_RemainingUses_WithLimit()
        {
            var coupon = MakeValidPercentCoupon();
            coupon.MaxRedemptions = 10;
            coupon.TimesUsed = 3;
            Assert.AreEqual(7, coupon.RemainingUses);
        }

        [TestMethod]
        public void Coupon_RemainingUses_Unlimited_ReturnsNull()
        {
            var coupon = MakeValidPercentCoupon();
            coupon.MaxRedemptions = null;
            Assert.IsNull(coupon.RemainingUses);
        }

        [TestMethod]
        public void Coupon_StatusDisplay_Active()
        {
            var coupon = MakeValidPercentCoupon();
            Assert.AreEqual("Active", coupon.StatusDisplay);
        }

        [TestMethod]
        public void Coupon_StatusDisplay_Disabled()
        {
            var coupon = MakeValidPercentCoupon();
            coupon.IsActive = false;
            Assert.AreEqual("Disabled", coupon.StatusDisplay);
        }

        [TestMethod]
        public void Coupon_StatusDisplay_Scheduled()
        {
            var coupon = MakeValidPercentCoupon();
            coupon.ValidFrom = DateTime.Today.AddDays(5);
            Assert.AreEqual("Scheduled", coupon.StatusDisplay);
        }

        [TestMethod]
        public void Coupon_StatusDisplay_Expired()
        {
            var coupon = MakeValidPercentCoupon();
            coupon.ValidUntil = DateTime.Today.AddDays(-1);
            Assert.AreEqual("Expired", coupon.StatusDisplay);
        }

        [TestMethod]
        public void Coupon_StatusDisplay_Exhausted()
        {
            var coupon = MakeValidPercentCoupon();
            coupon.MaxRedemptions = 5;
            coupon.TimesUsed = 5;
            Assert.AreEqual("Exhausted", coupon.StatusDisplay);
        }
    }
}
