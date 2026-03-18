using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Manages the movie waitlist — customers can join a queue for
    /// movies that are currently rented out and get notified by
    /// position when copies become available.
    /// </summary>
    public class WaitlistController : Controller
    {
        private readonly IWaitlistRepository _waitlistRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IMovieRepository _movieRepository;

        public WaitlistController()
            : this(
                new InMemoryWaitlistRepository(),
                new InMemoryCustomerRepository(),
                new InMemoryMovieRepository())
        {
        }

        public WaitlistController(
            IWaitlistRepository waitlistRepository,
            ICustomerRepository customerRepository,
            IMovieRepository movieRepository)
        {
            _waitlistRepository = waitlistRepository
                ?? throw new ArgumentNullException(nameof(waitlistRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
        }

        // GET: Waitlist
        public ActionResult Index(int? customerId, int? movieId, string message, bool? error)
        {
            var entries = _waitlistRepository.GetAll();

            if (customerId.HasValue)
                entries = _waitlistRepository.GetByCustomer(customerId.Value);
            if (movieId.HasValue)
                entries = _waitlistRepository.GetByMovie(movieId.Value);

            var viewModel = new WaitlistViewModel
            {
                Entries = entries,
                Customers = _customerRepository.GetAll(),
                Movies = _movieRepository.GetAll(),
                Stats = _waitlistRepository.GetStats(),
                SelectedCustomerId = customerId,
                SelectedMovieId = movieId,
                StatusMessage = message,
                IsError = error ?? false,
            };

            return View(viewModel);
        }

        // POST: Waitlist/Join
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Join(int customerId, int movieId, string priority, string note)
        {
            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                return RedirectToAction("Index", new { message = "Customer not found.", error = true });

            var movie = _movieRepository.GetById(movieId);
            if (movie == null)
                return RedirectToAction("Index", new { message = "Movie not found.", error = true });

            // Check for duplicate
            var existing = _waitlistRepository.FindExisting(customerId, movieId);
            if (existing != null)
                return RedirectToAction("Index", new
                {
                    message = $"{customer.Name} is already on the waitlist for \"{movie.Name}\" (position #{existing.Position}).",
                    error = true
                });

            var parsedPriority = WaitlistPriority.Normal;
            if (Enum.TryParse(priority, true, out WaitlistPriority p))
                parsedPriority = p;

            var entry = new WaitlistEntry
            {
                CustomerId = customerId,
                CustomerName = customer.Name,
                MovieId = movieId,
                MovieName = movie.Name,
                Priority = parsedPriority,
                Note = note?.Trim(),
            };

            _waitlistRepository.Add(entry);

            return RedirectToAction("Index", new
            {
                message = $"{customer.Name} joined the waitlist for \"{movie.Name}\" at position #{entry.Position}.",
            });
        }

        // POST: Waitlist/Cancel/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Cancel(int id)
        {
            var entry = _waitlistRepository.GetById(id);
            if (entry == null)
                return RedirectToAction("Index", new { message = "Waitlist entry not found.", error = true });

            entry.Status = WaitlistStatus.Cancelled;
            _waitlistRepository.Update(entry);
            _waitlistRepository.Remove(id);

            return RedirectToAction("Index", new
            {
                message = $"Removed {entry.CustomerName} from the waitlist for \"{entry.MovieName}\".",
            });
        }

        // POST: Waitlist/Notify/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Notify(int id)
        {
            var entry = _waitlistRepository.GetById(id);
            if (entry == null)
                return RedirectToAction("Index", new { message = "Waitlist entry not found.", error = true });

            if (entry.Status != WaitlistStatus.Waiting)
                return RedirectToAction("Index", new { message = "Only waiting entries can be notified.", error = true });

            entry.Status = WaitlistStatus.Notified;
            entry.NotifiedAt = DateTime.Now;
            entry.ExpiresAt = DateTime.Now.AddDays(2); // 48-hour window to pick up
            _waitlistRepository.Update(entry);

            return RedirectToAction("Index", new
            {
                message = $"Notified {entry.CustomerName} — \"{entry.MovieName}\" is available! They have 48 hours to pick it up.",
            });
        }

        // POST: Waitlist/Fulfill/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Fulfill(int id)
        {
            var entry = _waitlistRepository.GetById(id);
            if (entry == null)
                return RedirectToAction("Index", new { message = "Waitlist entry not found.", error = true });

            entry.Status = WaitlistStatus.Fulfilled;
            _waitlistRepository.Update(entry);

            return RedirectToAction("Index", new
            {
                message = $"{entry.CustomerName} picked up \"{entry.MovieName}\"! Waitlist entry fulfilled.",
            });
        }

        // GET: Waitlist/Stats
        public ActionResult Stats()
        {
            var stats = _waitlistRepository.GetStats();
            return Json(stats, JsonRequestBehavior.AllowGet);
        }
    }
}
