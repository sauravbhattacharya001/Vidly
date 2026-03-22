using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Browse, create, and track movie franchises/series.
    /// Customers can see their progress through multi-movie franchises
    /// and get recommendations for what to watch next.
    /// </summary>
    public class FranchiseController : Controller
    {
        private readonly FranchiseTrackerService _franchiseService;
        private readonly IMovieRepository _movieRepository;
        private readonly IRentalRepository _rentalRepository;
        private readonly ICustomerRepository _customerRepository;

        public FranchiseController()
            : this(
                new FranchiseTrackerService(),
                new InMemoryMovieRepository(),
                new InMemoryRentalRepository(),
                new InMemoryCustomerRepository())
        {
        }

        public FranchiseController(
            FranchiseTrackerService franchiseService,
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository,
            ICustomerRepository customerRepository)
        {
            _franchiseService = franchiseService
                ?? throw new ArgumentNullException(nameof(franchiseService));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));

            SeedDemoFranchises();
        }

        // GET: Franchise
        public ActionResult Index(string q)
        {
            var franchises = string.IsNullOrWhiteSpace(q)
                ? _franchiseService.GetAll()
                : _franchiseService.Search(q);

            var rentals = _rentalRepository.GetAll().ToList();
            var movies = _movieRepository.GetAll().ToList();

            var items = franchises.Select(f =>
            {
                var franchiseMovieSet = new HashSet<int>(f.MovieIds);
                var franchiseRentals = rentals.Count(r => franchiseMovieSet.Contains(r.MovieId));
                var franchiseMovies = movies.Where(m => franchiseMovieSet.Contains(m.Id)).ToList();
                var avgRating = franchiseMovies.Where(m => m.Rating.HasValue).Select(m => m.Rating.Value).DefaultIfEmpty(0).Average();

                return new FranchiseListItem
                {
                    Franchise = f,
                    MovieCount = f.MovieIds.Count,
                    TotalRentals = franchiseRentals,
                    AverageRating = Math.Round(avgRating, 1)
                };
            }).ToList();

            var viewModel = new FranchiseIndexViewModel
            {
                Franchises = items,
                SearchQuery = q,
                TotalCount = _franchiseService.GetAll().Count
            };

            return View(viewModel);
        }

        // GET: Franchise/Details/1
        public ActionResult Details(int id)
        {
            var franchise = _franchiseService.GetById(id);
            if (franchise == null)
                return HttpNotFound();

            var movies = _movieRepository.GetAll().ToList();
            var rentals = _rentalRepository.GetAll().ToList();
            var report = _franchiseService.GetReport(franchise, rentals, movies);

            var franchiseMovieSet = new HashSet<int>(franchise.MovieIds);
            var movieItems = new List<FranchiseMovieItem>();
            for (int i = 0; i < franchise.MovieIds.Count; i++)
            {
                var mid = franchise.MovieIds[i];
                var movie = movies.FirstOrDefault(m => m.Id == mid);
                movieItems.Add(new FranchiseMovieItem
                {
                    Position = i + 1,
                    Movie = movie ?? new Movie { Id = mid, Name = $"Movie #{mid} (not found)" },
                    RentalCount = rentals.Count(r => r.MovieId == mid)
                });
            }

            var viewModel = new FranchiseDetailViewModel
            {
                Franchise = franchise,
                Movies = movieItems,
                Report = report
            };

            return View(viewModel);
        }

        // GET: Franchise/Create
        public ActionResult Create()
        {
            var viewModel = new FranchiseFormViewModel
            {
                AvailableMovies = _movieRepository.GetAll()
                    .OrderBy(m => m.Name).ToList()
            };
            return View(viewModel);
        }

        // POST: Franchise/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(FranchiseFormViewModel model)
        {
            if (model == null)
                return new HttpStatusCodeResult(400);

            if (!ModelState.IsValid)
            {
                model.AvailableMovies = _movieRepository.GetAll()
                    .OrderBy(m => m.Name).ToList();
                return View(model);
            }

            try
            {
                var tags = !string.IsNullOrWhiteSpace(model.Tags)
                    ? model.Tags.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList()
                    : new List<string>();

                _franchiseService.Create(
                    model.Name,
                    model.SelectedMovieIds,
                    model.Description,
                    model.StartYear,
                    model.IsOngoing,
                    tags);

                TempData["Message"] = $"Franchise '{model.Name}' created!";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                model.AvailableMovies = _movieRepository.GetAll()
                    .OrderBy(m => m.Name).ToList();
                return View(model);
            }
        }

        // GET: Franchise/Progress?customerId=1
        public ActionResult Progress(int? customerId)
        {
            var customers = _customerRepository.GetAll()
                .OrderBy(c => c.Name).ToList();
            ViewBag.Customers = customers
                .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name })
                .ToList();

            if (!customerId.HasValue)
                return View(new FranchiseProgressViewModel());

            var customer = customers.FirstOrDefault(c => c.Id == customerId.Value);
            if (customer == null)
                return HttpNotFound();

            var rentals = _rentalRepository.GetAll().ToList();
            var movies = _movieRepository.GetAll().ToList();
            var allProgress = _franchiseService.GetAllProgress(customerId.Value, rentals);
            var recommendations = _franchiseService.GetRecommendations(customerId.Value, rentals, movies);

            var franchiseNames = _franchiseService.GetAll()
                .ToDictionary(f => f.Id, f => f.Name);

            var viewModel = new FranchiseProgressViewModel
            {
                CustomerId = customerId.Value,
                CustomerName = customer.Name,
                InProgress = allProgress.Where(p => !p.CompletedDate.HasValue).ToList(),
                Completed = allProgress.Where(p => p.CompletedDate.HasValue).ToList(),
                Recommendations = recommendations,
                FranchiseNames = franchiseNames
            };

            return View(viewModel);
        }

        // GET: Franchise/Popular
        public ActionResult Popular()
        {
            var rentals = _rentalRepository.GetAll().ToList();
            var popular = _franchiseService.GetPopularFranchises(rentals, 10);
            var movies = _movieRepository.GetAll().ToList();

            var items = popular.Select(f =>
            {
                var franchiseMovieSet = new HashSet<int>(f.MovieIds);
                return new FranchiseListItem
                {
                    Franchise = f,
                    MovieCount = f.MovieIds.Count,
                    TotalRentals = rentals.Count(r => franchiseMovieSet.Contains(r.MovieId)),
                    AverageRating = Math.Round(
                        movies.Where(m => franchiseMovieSet.Contains(m.Id) && m.Rating.HasValue)
                              .Select(m => m.Rating.Value).DefaultIfEmpty(0).Average(), 1)
                };
            }).ToList();

            return View(items);
        }

        // POST: Franchise/Delete/1
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            var franchise = _franchiseService.GetById(id);
            if (franchise == null)
                return HttpNotFound();

            _franchiseService.Delete(id);
            TempData["Message"] = $"Franchise '{franchise.Name}' deleted.";
            return RedirectToAction("Index");
        }

        /// <summary>
        /// Seeds sample franchises if none exist (for demo purposes).
        /// </summary>
        private void SeedDemoFranchises()
        {
            if (_franchiseService.GetAll().Count > 0) return;

            var movies = _movieRepository.GetAll().ToList();
            if (movies.Count < 6) return;

            // Create a couple of demo franchises from available movies
            try
            {
                var actionMovies = movies.Where(m => m.Genre == Genre.Action).Take(3).ToList();
                if (actionMovies.Count >= 2)
                {
                    _franchiseService.Create(
                        "Action Trilogy",
                        actionMovies.Select(m => m.Id).ToList(),
                        "The ultimate action movie marathon",
                        actionMovies.Min(m => m.ReleaseDate?.Year),
                        false,
                        new List<string> { "action", "blockbuster" });
                }

                var sciFiMovies = movies.Where(m => m.Genre == Genre.ScienceFiction).Take(3).ToList();
                if (sciFiMovies.Count >= 2)
                {
                    _franchiseService.Create(
                        "Sci-Fi Saga",
                        sciFiMovies.Select(m => m.Id).ToList(),
                        "Journey through the stars",
                        sciFiMovies.Min(m => m.ReleaseDate?.Year),
                        true,
                        new List<string> { "sci-fi", "space" });
                }
            }
            catch
            {
                // Seeding is best-effort
            }
        }
    }
}
