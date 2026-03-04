using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Manages bundle deals for multi-movie rental discounts.
    /// </summary>
    public class BundlesController : Controller
    {
        private readonly BundleService _bundleService;
        private readonly IMovieRepository _movieRepository;

        public BundlesController()
            : this(new BundleService(), new InMemoryMovieRepository())
        {
        }

        public BundlesController(BundleService bundleService, IMovieRepository movieRepository)
        {
            _bundleService = bundleService ?? throw new ArgumentNullException(nameof(bundleService));
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
        }

        // GET: Bundles
        public ActionResult Index()
        {
            var bundles = _bundleService.GetAll();
            var stats = _bundleService.GetStats();
            var viewModel = new BundleIndexViewModel
            {
                Bundles = bundles,
                Stats = stats
            };
            return View(viewModel);
        }

        // GET: Bundles/Create
        public ActionResult Create()
        {
            var viewModel = new BundleDeal
            {
                MinMovies = 2,
                DiscountType = BundleDiscountType.Percentage,
                DiscountValue = 10,
                IsActive = true
            };
            return View(viewModel);
        }

        // POST: Bundles/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(BundleDeal bundle)
        {
            if (!ModelState.IsValid)
                return View(bundle);

            try
            {
                _bundleService.Add(bundle);
                TempData["Message"] = $"Bundle '{bundle.Name}' created successfully!";
                return RedirectToAction("Index");
            }
            catch (ArgumentException ex)
            {
                ModelState.AddModelError("", ex.Message);
                return View(bundle);
            }
        }

        // POST: Bundles/Toggle/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Toggle(int id)
        {
            var bundle = _bundleService.GetById(id);
            if (bundle == null) return HttpNotFound();

            bundle.IsActive = !bundle.IsActive;
            _bundleService.Update(bundle);

            TempData["Message"] = bundle.IsActive
                ? $"Bundle '{bundle.Name}' activated."
                : $"Bundle '{bundle.Name}' deactivated.";

            return RedirectToAction("Index");
        }

        // POST: Bundles/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            try
            {
                var bundle = _bundleService.GetById(id);
                _bundleService.Remove(id);
                TempData["Message"] = $"Bundle '{bundle?.Name}' deleted.";
            }
            catch (KeyNotFoundException)
            {
                return HttpNotFound();
            }

            return RedirectToAction("Index");
        }

        // GET: Bundles/Calculator
        public ActionResult Calculator()
        {
            var movies = _movieRepository.GetAll();
            var bundles = _bundleService.GetActive();
            var viewModel = new BundleCalculatorViewModel
            {
                AvailableMovies = movies,
                ActiveBundles = bundles,
                SelectedMovieIds = new List<int>()
            };
            return View(viewModel);
        }

        // POST: Bundles/Calculator
        [HttpPost]
        public ActionResult Calculator(BundleCalculatorViewModel viewModel)
        {
            var movies = _movieRepository.GetAll();
            var bundles = _bundleService.GetActive();

            viewModel.AvailableMovies = movies;
            viewModel.ActiveBundles = bundles;

            if (viewModel.SelectedMovieIds != null && viewModel.SelectedMovieIds.Count > 0)
            {
                var selectedMovies = movies
                    .Where(m => viewModel.SelectedMovieIds.Contains(m.Id))
                    .ToList();

                var dailyRates = new Dictionary<int, decimal>();
                foreach (var movie in selectedMovies)
                {
                    dailyRates[movie.Id] = PricingService.GetMovieDailyRate(movie);
                }

                viewModel.Result = _bundleService.FindBestBundle(selectedMovies, dailyRates, viewModel.RentalDays);
            }

            return View(viewModel);
        }
    }

    public class BundleIndexViewModel
    {
        public IReadOnlyList<BundleDeal> Bundles { get; set; }
        public BundleStats Stats { get; set; }
    }

    public class BundleCalculatorViewModel
    {
        public IReadOnlyList<Movie> AvailableMovies { get; set; }
        public IReadOnlyList<BundleDeal> ActiveBundles { get; set; }
        public List<int> SelectedMovieIds { get; set; } = new List<int>();
        public int RentalDays { get; set; } = 3;
        public BundleApplyResult Result { get; set; }
    }
}
