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
    /// Manages seasonal and holiday promotions: list, create, edit, details, delete.
    /// </summary>
    public class PromotionsController : Controller
    {
        private readonly IPromotionRepository _promotionRepository;
        private readonly IMovieRepository _movieRepository;

        public PromotionsController()
            : this(new InMemoryPromotionRepository(), new InMemoryMovieRepository()) { }

        public PromotionsController(
            IPromotionRepository promotionRepository,
            IMovieRepository movieRepository)
        {
            _promotionRepository = promotionRepository
                ?? throw new ArgumentNullException(nameof(promotionRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
        }

        // GET: Promotions
        public ActionResult Index(string status)
        {
            var all = _promotionRepository.GetAll();

            IReadOnlyList<Promotion> filtered;
            if (!string.IsNullOrWhiteSpace(status))
                filtered = all.Where(p =>
                    string.Equals(p.StatusDisplay, status, StringComparison.OrdinalIgnoreCase))
                    .ToList().AsReadOnly();
            else
                filtered = all;

            var vm = new PromotionIndexViewModel
            {
                Promotions = filtered,
                StatusFilter = status,
                TotalCount = all.Count,
                ActiveCount = all.Count(p => p.StatusDisplay == "Active"),
                UpcomingCount = all.Count(p => p.StatusDisplay == "Upcoming"),
                ExpiredCount = all.Count(p => p.StatusDisplay == "Expired")
            };

            return View(vm);
        }

        // GET: Promotions/Details/3
        public ActionResult Details(int id)
        {
            var promo = _promotionRepository.GetById(id);
            if (promo == null) return HttpNotFound();

            var movieIds = promo.GetFeaturedMovieIdList();
            var allMovies = _movieRepository.GetAll();
            var featured = allMovies.Where(m => movieIds.Contains(m.Id)).ToList();

            var vm = new PromotionDetailsViewModel
            {
                Promotion = promo,
                FeaturedMovies = featured.AsReadOnly()
            };

            return View(vm);
        }

        // GET: Promotions/Create
        public ActionResult Create()
        {
            ViewBag.Movies = _movieRepository.GetAll();
            var promo = new Promotion
            {
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(30),
                DiscountPercent = 10,
                BannerColor = "#e74c3c"
            };
            return View(promo);
        }

        // POST: Promotions/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(Promotion promotion)
        {
            if (promotion == null) return new HttpStatusCodeResult(400);
            ValidatePromotion(promotion);
            if (!ModelState.IsValid)
            {
                ViewBag.Movies = _movieRepository.GetAll();
                return View(promotion);
            }

            _promotionRepository.Add(promotion);
            TempData["Message"] = $"Promotion '{promotion.Name}' created!";
            return RedirectToAction("Index");
        }

        // GET: Promotions/Edit/3
        public ActionResult Edit(int id)
        {
            var promo = _promotionRepository.GetById(id);
            if (promo == null) return HttpNotFound();
            ViewBag.Movies = _movieRepository.GetAll();
            return View(promo);
        }

        // POST: Promotions/Edit/3
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(Promotion promotion)
        {
            if (promotion == null) return new HttpStatusCodeResult(400);
            ValidatePromotion(promotion);
            if (!ModelState.IsValid)
            {
                ViewBag.Movies = _movieRepository.GetAll();
                return View(promotion);
            }

            try
            {
                _promotionRepository.Update(promotion);
                TempData["Message"] = $"Promotion '{promotion.Name}' updated.";
            }
            catch (KeyNotFoundException)
            {
                return HttpNotFound();
            }

            return RedirectToAction("Index");
        }

        // POST: Promotions/Delete/3
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            try
            {
                _promotionRepository.Remove(id);
                TempData["Message"] = "Promotion deleted.";
            }
            catch (KeyNotFoundException)
            {
                return HttpNotFound();
            }

            return RedirectToAction("Index");
        }

        private void ValidatePromotion(Promotion p)
        {
            if (p.EndDate < p.StartDate)
                ModelState.AddModelError("EndDate", "End date must be after start date.");
        }
    }
}
