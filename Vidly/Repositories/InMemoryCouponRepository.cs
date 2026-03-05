using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Thread-safe in-memory coupon repository with seed data.
    /// </summary>
    public class InMemoryCouponRepository : ICouponRepository
    {
        private static readonly object _lock = new object();
        private static readonly List<Coupon> _coupons = new List<Coupon>();
        private static int _nextId = 1;
        private static bool _seeded;

        public InMemoryCouponRepository()
        {
            lock (_lock)
            {
                if (!_seeded)
                {
                    Seed();
                    _seeded = true;
                }
            }
        }

        private static void Seed()
        {
            var now = DateTime.Today;
            _coupons.AddRange(new[]
            {
                new Coupon
                {
                    Id = _nextId++,
                    Code = "WELCOME20",
                    Description = "20% off your first rental — welcome to Vidly!",
                    DiscountType = DiscountType.Percentage,
                    DiscountValue = 20,
                    MinimumOrderAmount = 0,
                    MaxDiscountAmount = 10.00m,
                    ValidFrom = now.AddDays(-30),
                    ValidUntil = now.AddDays(60),
                    MaxRedemptions = 100,
                    TimesUsed = 12,
                    IsActive = true,
                    CreatedDate = now.AddDays(-30)
                },
                new Coupon
                {
                    Id = _nextId++,
                    Code = "SPRING50",
                    Description = "50% off any rental — spring special!",
                    DiscountType = DiscountType.Percentage,
                    DiscountValue = 50,
                    MinimumOrderAmount = 5.00m,
                    MaxDiscountAmount = 15.00m,
                    ValidFrom = now.AddDays(-5),
                    ValidUntil = now.AddDays(25),
                    MaxRedemptions = 50,
                    TimesUsed = 3,
                    IsActive = true,
                    CreatedDate = now.AddDays(-5)
                },
                new Coupon
                {
                    Id = _nextId++,
                    Code = "FLAT2OFF",
                    Description = "$2.00 off any rental",
                    DiscountType = DiscountType.FixedAmount,
                    DiscountValue = 2.00m,
                    MinimumOrderAmount = 3.00m,
                    MaxDiscountAmount = null,
                    ValidFrom = now.AddDays(-10),
                    ValidUntil = now.AddDays(90),
                    MaxRedemptions = null,
                    TimesUsed = 27,
                    IsActive = true,
                    CreatedDate = now.AddDays(-10)
                },
                new Coupon
                {
                    Id = _nextId++,
                    Code = "EXPIRED10",
                    Description = "10% off — expired promo",
                    DiscountType = DiscountType.Percentage,
                    DiscountValue = 10,
                    MinimumOrderAmount = 0,
                    MaxDiscountAmount = null,
                    ValidFrom = now.AddDays(-60),
                    ValidUntil = now.AddDays(-5),
                    MaxRedemptions = null,
                    TimesUsed = 45,
                    IsActive = true,
                    CreatedDate = now.AddDays(-60)
                },
                new Coupon
                {
                    Id = _nextId++,
                    Code = "VIP30",
                    Description = "30% off for VIP customers",
                    DiscountType = DiscountType.Percentage,
                    DiscountValue = 30,
                    MinimumOrderAmount = 10.00m,
                    MaxDiscountAmount = 20.00m,
                    ValidFrom = now,
                    ValidUntil = now.AddDays(180),
                    MaxRedemptions = 25,
                    TimesUsed = 0,
                    IsActive = true,
                    CreatedDate = now
                }
            });
        }

        public IReadOnlyList<Coupon> GetAll()
        {
            lock (_lock)
            {
                return _coupons.ToList().AsReadOnly();
            }
        }

        public Coupon GetById(int id)
        {
            lock (_lock)
            {
                return _coupons.FirstOrDefault(c => c.Id == id);
            }
        }

        public Coupon GetByCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            lock (_lock)
            {
                return _coupons.FirstOrDefault(c =>
                    string.Equals(c.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
            }
        }

        public void Add(Coupon coupon)
        {
            if (coupon == null) throw new ArgumentNullException(nameof(coupon));
            lock (_lock)
            {
                // Prevent duplicate codes
                if (_coupons.Any(c =>
                    string.Equals(c.Code, coupon.Code, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException(
                        $"A coupon with code '{coupon.Code}' already exists.");
                }

                coupon.Id = _nextId++;
                coupon.CreatedDate = DateTime.Now;
                _coupons.Add(coupon);
            }
        }

        public void Update(Coupon coupon)
        {
            if (coupon == null) throw new ArgumentNullException(nameof(coupon));
            lock (_lock)
            {
                var existing = _coupons.FirstOrDefault(c => c.Id == coupon.Id);
                if (existing == null)
                    throw new KeyNotFoundException($"Coupon {coupon.Id} not found.");

                // Check for code collision with another coupon
                if (_coupons.Any(c => c.Id != coupon.Id &&
                    string.Equals(c.Code, coupon.Code, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException(
                        $"Another coupon with code '{coupon.Code}' already exists.");
                }

                existing.Code = coupon.Code;
                existing.Description = coupon.Description;
                existing.DiscountType = coupon.DiscountType;
                existing.DiscountValue = coupon.DiscountValue;
                existing.MinimumOrderAmount = coupon.MinimumOrderAmount;
                existing.MaxDiscountAmount = coupon.MaxDiscountAmount;
                existing.ValidFrom = coupon.ValidFrom;
                existing.ValidUntil = coupon.ValidUntil;
                existing.MaxRedemptions = coupon.MaxRedemptions;
                existing.IsActive = coupon.IsActive;
            }
        }

        public void Remove(int id)
        {
            lock (_lock)
            {
                var coupon = _coupons.FirstOrDefault(c => c.Id == id);
                if (coupon == null)
                    throw new KeyNotFoundException($"Coupon {id} not found.");
                _coupons.Remove(coupon);
            }
        }

        public bool TryRedeem(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            lock (_lock)
            {
                var coupon = _coupons.FirstOrDefault(c =>
                    string.Equals(c.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

                if (coupon == null || !coupon.IsValid) return false;

                coupon.TimesUsed++;
                return true;
            }
        }

        /// <summary>
        /// Resets the repository to its initial seed state for test isolation.
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _coupons.Clear();
                _nextId = 1;
                _seeded = false;
            }
        }
    }
}
