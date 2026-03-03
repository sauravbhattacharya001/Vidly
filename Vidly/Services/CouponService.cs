using System;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Validates and applies promotional coupons to rental checkouts.
    /// </summary>
    public class CouponService
    {
        private readonly ICouponRepository _couponRepository;

        public CouponService() : this(new InMemoryCouponRepository()) { }

        public CouponService(ICouponRepository couponRepository)
        {
            _couponRepository = couponRepository
                ?? throw new ArgumentNullException(nameof(couponRepository));
        }

        /// <summary>
        /// Validate a coupon code against a rental subtotal and return
        /// a result describing the discount or the reason it's invalid.
        /// Does NOT redeem — call Apply() to actually use it.
        /// </summary>
        public CouponValidationResult Validate(string code, decimal rentalSubtotal)
        {
            if (string.IsNullOrWhiteSpace(code))
                return CouponValidationResult.Fail("Please enter a coupon code.");

            var coupon = _couponRepository.GetByCode(code.Trim());
            if (coupon == null)
                return CouponValidationResult.Fail("Coupon code not found.");

            if (!coupon.IsActive)
                return CouponValidationResult.Fail("This coupon has been disabled.");

            if (DateTime.Today < coupon.ValidFrom)
                return CouponValidationResult.Fail(
                    $"This coupon is not valid until {coupon.ValidFrom:MMM d, yyyy}.");

            if (DateTime.Today > coupon.ValidUntil)
                return CouponValidationResult.Fail("This coupon has expired.");

            if (coupon.MaxRedemptions.HasValue && coupon.TimesUsed >= coupon.MaxRedemptions.Value)
                return CouponValidationResult.Fail("This coupon has reached its redemption limit.");

            if (rentalSubtotal < coupon.MinimumOrderAmount)
                return CouponValidationResult.Fail(
                    $"Minimum order of ${coupon.MinimumOrderAmount:F2} required for this coupon.");

            var discount = CalculateDiscount(coupon, rentalSubtotal);

            return CouponValidationResult.Success(coupon, discount);
        }

        /// <summary>
        /// Apply (redeem) a coupon, incrementing its usage count.
        /// Returns the discount amount, or 0 if redemption fails.
        /// </summary>
        public decimal Apply(string code, decimal rentalSubtotal)
        {
            var validation = Validate(code, rentalSubtotal);
            if (!validation.IsValid) return 0m;

            var redeemed = _couponRepository.TryRedeem(code);
            return redeemed ? validation.DiscountAmount : 0m;
        }

        private static decimal CalculateDiscount(Coupon coupon, decimal subtotal)
        {
            decimal discount;
            if (coupon.DiscountType == DiscountType.Percentage)
            {
                discount = subtotal * coupon.DiscountValue / 100m;
                if (coupon.MaxDiscountAmount.HasValue)
                    discount = Math.Min(discount, coupon.MaxDiscountAmount.Value);
            }
            else
            {
                discount = coupon.DiscountValue;
            }

            // Never discount more than the subtotal
            return Math.Min(Math.Round(discount, 2), subtotal);
        }
    }

    /// <summary>
    /// Result of coupon validation.
    /// </summary>
    public class CouponValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; }
        public string CouponCode { get; set; }
        public string Description { get; set; }
        public DiscountType? DiscountType { get; set; }
        public decimal DiscountValue { get; set; }
        public decimal DiscountAmount { get; set; }

        public static CouponValidationResult Fail(string message) =>
            new CouponValidationResult { IsValid = false, Message = message };

        public static CouponValidationResult Success(Coupon coupon, decimal discountAmount) =>
            new CouponValidationResult
            {
                IsValid = true,
                Message = coupon.DiscountType == Models.DiscountType.Percentage
                    ? $"{coupon.DiscountValue}% off — save ${discountAmount:F2}!"
                    : $"${discountAmount:F2} off!",
                CouponCode = coupon.Code,
                Description = coupon.Description,
                DiscountType = coupon.DiscountType,
                DiscountValue = coupon.DiscountValue,
                DiscountAmount = discountAmount
            };
    }
}
