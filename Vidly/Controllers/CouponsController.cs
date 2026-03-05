using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Manages promotional coupons: list, create, edit, toggle, delete.
    /// </summary>
    public class CouponsController : Controller
    {
        private readonly ICouponRepository _couponRepository;

        public CouponsController() : this(new InMemoryCouponRepository()) { }

        public CouponsController(ICouponRepository couponRepository)
        {
            _couponRepository = couponRepository
                ?? throw new ArgumentNullException(nameof(couponRepository));
        }

        // GET: Coupons
        public ActionResult Index(string status)
        {
            // Single GetAll() call — avoids 4 redundant repository round-trips
            var allCoupons = _couponRepository.GetAll();
            var totalCount = allCoupons.Count;

            IReadOnlyList<Coupon> coupons;
            if (!string.IsNullOrWhiteSpace(status))
            {
                coupons = allCoupons.Where(c =>
                    string.Equals(c.StatusDisplay, status, StringComparison.OrdinalIgnoreCase))
                    .ToList().AsReadOnly();
            }
            else
            {
                coupons = allCoupons;
            }

            var viewModel = new CouponIndexViewModel
            {
                Coupons = coupons,
                StatusFilter = status,
                TotalCount = totalCount,
                ActiveCount = allCoupons.Count(c => c.StatusDisplay == "Active"),
                ExpiredCount = allCoupons.Count(c => c.StatusDisplay == "Expired"),
                ExhaustedCount = allCoupons.Count(c => c.StatusDisplay == "Exhausted")
            };

            return View(viewModel);
        }

        // GET: Coupons/Create
        public ActionResult Create()
        {
            var coupon = new Coupon
            {
                ValidFrom = DateTime.Today,
                ValidUntil = DateTime.Today.AddDays(30),
                IsActive = true,
                DiscountType = DiscountType.Percentage
            };
            return View(coupon);
        }

        // POST: Coupons/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(Coupon coupon)
        {
            if (coupon == null)
                return new HttpStatusCodeResult(400);

            // Force uppercase
            if (!string.IsNullOrWhiteSpace(coupon.Code))
                coupon.Code = coupon.Code.Trim().ToUpperInvariant();

            if (coupon.ValidUntil < coupon.ValidFrom)
                ModelState.AddModelError("ValidUntil", "Expiration must be after start date.");

            if (coupon.DiscountType == DiscountType.FixedAmount && coupon.DiscountValue > 100)
                ModelState.AddModelError("DiscountValue",
                    "Fixed discount cannot exceed $100.00.");

            if (!ModelState.IsValid)
                return View(coupon);

            try
            {
                _couponRepository.Add(coupon);
                TempData["Message"] = $"Coupon '{coupon.Code}' created successfully!";
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError("Code", ex.Message);
                return View(coupon);
            }

            return RedirectToAction("Index");
        }

        // GET: Coupons/Edit/5
        public ActionResult Edit(int id)
        {
            var coupon = _couponRepository.GetById(id);
            if (coupon == null)
                return HttpNotFound();
            return View(coupon);
        }

        // POST: Coupons/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(Coupon coupon)
        {
            if (coupon == null)
                return new HttpStatusCodeResult(400);

            if (!string.IsNullOrWhiteSpace(coupon.Code))
                coupon.Code = coupon.Code.Trim().ToUpperInvariant();

            if (coupon.ValidUntil < coupon.ValidFrom)
                ModelState.AddModelError("ValidUntil", "Expiration must be after start date.");

            if (coupon.DiscountType == DiscountType.FixedAmount && coupon.DiscountValue > 100)
                ModelState.AddModelError("DiscountValue",
                    "Fixed discount cannot exceed $100.00.");

            if (!ModelState.IsValid)
                return View(coupon);

            try
            {
                _couponRepository.Update(coupon);
                TempData["Message"] = $"Coupon '{coupon.Code}' updated.";
            }
            catch (Exception ex) when (ex is KeyNotFoundException || ex is InvalidOperationException)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction("Index");
        }

        // POST: Coupons/Toggle/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Toggle(int id)
        {
            var coupon = _couponRepository.GetById(id);
            if (coupon == null)
                return HttpNotFound();

            coupon.IsActive = !coupon.IsActive;
            _couponRepository.Update(coupon);
            TempData["Message"] = $"Coupon '{coupon.Code}' {(coupon.IsActive ? "enabled" : "disabled")}.";

            return RedirectToAction("Index");
        }

        // POST: Coupons/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            try
            {
                var coupon = _couponRepository.GetById(id);
                _couponRepository.Remove(id);
                TempData["Message"] = $"Coupon '{coupon?.Code}' deleted.";
            }
            catch (KeyNotFoundException)
            {
                return HttpNotFound();
            }

            return RedirectToAction("Index");
        }
    }
}
