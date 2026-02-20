using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    public class WatchlistController : Controller
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly IWatchlistRepository _watchlistRepository;

        /// <summary>
        /// Parameterless constructor for ASP.NET MVC default controller factory.
        /// </summary>
        public WatchlistController()
            : this(
                new InMemoryCustomerRepository(),
                new InMemoryMovieRepository(),
                new InMemoryWatchlistRepository())
        {
        }

        /// <summary>
        /// Constructor injection for testability and future DI container use.
        /// </summary>
        public WatchlistController(
            ICustomerRepository customerRepository,
            IMovieRepository movieRepository,
            IWatchlistRepository watchlistRepository)
        {
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _watchlistRepository = watchlistRepository
                ?? throw new ArgumentNullException(nameof(watchlistRepository));
        }

        // GET: Watchlist
        public ActionResult Index(int? customerId, string message, bool? error)
        {
            var viewModel = new WatchlistViewModel
            {
                Customers = _customerRepository.GetAll(),
                SelectedCustomerId = customerId,
                PopularMovies = _watchlistRepository.GetMostWatchlisted(5),
                StatusMessage = message,
                IsError = error ?? false
            };

            if (customerId.HasValue)
            {
                var customer = _customerRepository.GetById(customerId.Value);
                if (customer == null)
                    return HttpNotFound("Customer not found.");

                viewModel.SelectedCustomerName = customer.Name;
                viewModel.Items = _watchlistRepository.GetByCustomer(customerId.Value);
                viewModel.Stats = _watchlistRepository.GetStats(customerId.Value);
            }

            return View(viewModel);
        }

        // GET: Watchlist/Add?customerId=1&movieId=2
        public ActionResult Add(int? customerId, int? movieId)
        {
            var allMovies = _movieRepository.GetAll();
            var availableMovies = allMovies;

            // If a customer is pre-selected, filter out movies already on their watchlist
            if (customerId.HasValue)
            {
                var watchlistItems = _watchlistRepository.GetByCustomer(customerId.Value);
                var watchlistMovieIds = watchlistItems.Select(w => w.MovieId).ToHashSet();
                availableMovies = allMovies.Where(m => !watchlistMovieIds.Contains(m.Id)).ToList().AsReadOnly();
            }

            var viewModel = new WatchlistAddViewModel
            {
                Customers = _customerRepository.GetAll(),
                AvailableMovies = availableMovies,
                SelectedCustomerId = customerId,
                SelectedMovieId = movieId,
                Item = new WatchlistItem
                {
                    CustomerId = customerId ?? 0,
                    MovieId = movieId ?? 0,
                    Priority = WatchlistPriority.Normal
                }
            };

            return View(viewModel);
        }

        // POST: Watchlist/Add
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Add(WatchlistItem item)
        {
            if (item.CustomerId <= 0)
                ModelState.AddModelError("CustomerId", "Please select a customer.");
            if (item.MovieId <= 0)
                ModelState.AddModelError("MovieId", "Please select a movie.");

            if (!ModelState.IsValid)
            {
                var allMovies = _movieRepository.GetAll();
                var viewModel = new WatchlistAddViewModel
                {
                    Customers = _customerRepository.GetAll(),
                    AvailableMovies = allMovies,
                    SelectedCustomerId = item.CustomerId > 0 ? item.CustomerId : (int?)null,
                    SelectedMovieId = item.MovieId > 0 ? item.MovieId : (int?)null,
                    Item = item
                };
                return View(viewModel);
            }

            // Resolve names
            var customer = _customerRepository.GetById(item.CustomerId);
            if (customer == null)
                return HttpNotFound("Customer not found.");

            var movie = _movieRepository.GetById(item.MovieId);
            if (movie == null)
                return HttpNotFound("Movie not found.");

            item.CustomerName = customer.Name;
            item.MovieName = movie.Name;
            item.MovieGenre = movie.Genre;
            item.MovieRating = movie.Rating;
            item.AddedDate = DateTime.Today;

            try
            {
                _watchlistRepository.Add(item);
                return RedirectToAction("Index", new
                {
                    customerId = item.CustomerId,
                    message = $"'{movie.Name}' added to watchlist!"
                });
            }
            catch (InvalidOperationException ex)
            {
                return RedirectToAction("Index", new
                {
                    customerId = item.CustomerId,
                    message = ex.Message,
                    error = true
                });
            }
        }

        // POST: Watchlist/Remove/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Remove(int id, int? customerId)
        {
            try
            {
                var item = _watchlistRepository.GetById(id);
                _watchlistRepository.Remove(id);

                var returnCustomerId = customerId ?? item?.CustomerId;
                return RedirectToAction("Index", new
                {
                    customerId = returnCustomerId,
                    message = item != null
                        ? $"'{item.MovieName}' removed from watchlist."
                        : "Item removed from watchlist."
                });
            }
            catch (KeyNotFoundException)
            {
                return RedirectToAction("Index", new
                {
                    customerId,
                    message = "Watchlist item not found.",
                    error = true
                });
            }
        }

        // POST: Watchlist/Clear?customerId=1
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Clear(int customerId)
        {
            var count = _watchlistRepository.ClearCustomerWatchlist(customerId);
            return RedirectToAction("Index", new
            {
                customerId,
                message = count > 0
                    ? $"Cleared {count} item(s) from watchlist."
                    : "Watchlist was already empty."
            });
        }

        // POST: Watchlist/UpdatePriority/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult UpdatePriority(int id, WatchlistPriority priority, int? customerId)
        {
            try
            {
                var item = _watchlistRepository.GetById(id);
                if (item == null)
                    return HttpNotFound("Watchlist item not found.");

                item.Priority = priority;
                _watchlistRepository.Update(item);

                return RedirectToAction("Index", new
                {
                    customerId = customerId ?? item.CustomerId,
                    message = $"Priority updated for '{item.MovieName}'."
                });
            }
            catch (KeyNotFoundException)
            {
                return RedirectToAction("Index", new
                {
                    customerId,
                    message = "Watchlist item not found.",
                    error = true
                });
            }
        }
    }
}
