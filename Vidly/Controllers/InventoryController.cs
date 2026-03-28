using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Controllers
{
    /// <summary>
    /// Inventory Dashboard – shows stock levels, low-stock alerts, and
    /// utilization metrics for every movie title in the store.
    /// </summary>
    public class InventoryController : Controller
    {
        private readonly IMovieRepository _movieRepo;
        private readonly IRentalRepository _rentalRepo;

        // Default stock per title (simulated — a real app would have a Stock table)
        private const int DefaultTotalCopies = 5;
        private const int DefaultThreshold = 1;

        // In-memory threshold overrides (keyed by movieId)
        private static readonly Dictionary<int, int> _thresholds = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> _stockOverrides = new Dictionary<int, int>();
        private static readonly object _lock = new object();

        public InventoryController()
            : this(new InMemoryMovieRepository(), new InMemoryRentalRepository()) { }

        public InventoryController(IMovieRepository movieRepo, IRentalRepository rentalRepo)
        {
            _movieRepo = movieRepo;
            _rentalRepo = rentalRepo;
        }

        // GET: /Inventory
        public ActionResult Index(string sort = "name", string filter = "all", string search = "")
        {
            var movies = _movieRepo.GetAll();
            var rentals = _rentalRepo.GetAll();

            // Count active (not returned) rentals per movie
            var rentedCounts = rentals
                .Where(r => !r.DateReturned.HasValue)
                .GroupBy(r => r.MovieId)
                .ToDictionary(g => g.Key, g => g.Count());

            var items = movies.Select(m =>
            {
                int totalCopies;
                int threshold;
                lock (_lock)
                {
                    totalCopies = _stockOverrides.ContainsKey(m.Id) ? _stockOverrides[m.Id] : DefaultTotalCopies;
                    threshold = _thresholds.ContainsKey(m.Id) ? _thresholds[m.Id] : DefaultThreshold;
                }

                int rented = rentedCounts.ContainsKey(m.Id) ? rentedCounts[m.Id] : 0;

                return new InventoryItem
                {
                    MovieId = m.Id,
                    MovieName = m.Name,
                    Genre = m.Genre?.ToString() ?? "Unknown",
                    TotalCopies = totalCopies,
                    RentedOut = Math.Min(rented, totalCopies),
                    Threshold = threshold
                };
            }).ToList();

            // Search
            if (!string.IsNullOrWhiteSpace(search))
                items = items.Where(i => i.MovieName != null &&
                    i.MovieName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            // Filter
            switch (filter?.ToLowerInvariant())
            {
                case "low":
                    items = items.Where(i => i.IsLowStock).ToList();
                    break;
                case "out":
                    items = items.Where(i => i.Available == 0).ToList();
                    break;
            }

            // Sort
            switch (sort?.ToLowerInvariant())
            {
                case "available":
                    items = items.OrderBy(i => i.Available).ThenBy(i => i.MovieName).ToList();
                    break;
                case "utilization":
                    items = items.OrderByDescending(i => i.Utilization).ThenBy(i => i.MovieName).ToList();
                    break;
                case "rented":
                    items = items.OrderByDescending(i => i.RentedOut).ThenBy(i => i.MovieName).ToList();
                    break;
                default:
                    items = items.OrderBy(i => i.MovieName).ToList();
                    break;
            }

            ViewBag.CurrentSort = sort;
            ViewBag.CurrentFilter = filter;
            ViewBag.CurrentSearch = search ?? "";
            ViewBag.TotalMovies = items.Count;
            ViewBag.LowStockCount = items.Count(i => i.IsLowStock);
            ViewBag.OutOfStockCount = items.Count(i => i.Available == 0);
            ViewBag.AvgUtilization = items.Any() ? (int)items.Average(i => i.Utilization) : 0;

            return View(items);
        }

        // POST: /Inventory/UpdateThreshold
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult UpdateThreshold(int movieId, int threshold)
        {
            if (threshold < 0) threshold = 0;
            if (threshold > 99) threshold = 99;

            lock (_lock)
            {
                _thresholds[movieId] = threshold;
            }

            TempData["Success"] = "Threshold updated.";
            return RedirectToAction("Index");
        }

        // POST: /Inventory/UpdateStock
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult UpdateStock(int movieId, int totalCopies)
        {
            if (totalCopies < 1) totalCopies = 1;
            if (totalCopies > 999) totalCopies = 999;

            lock (_lock)
            {
                _stockOverrides[movieId] = totalCopies;
            }

            TempData["Success"] = "Stock updated.";
            return RedirectToAction("Index");
        }
    }
}
