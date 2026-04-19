using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Movie Autopilot — autonomous weekly rental queue curation
    /// that learns from accept/skip feedback.
    /// </summary>
    public class AutopilotController : Controller
    {
        private readonly AutopilotService _service;
        private readonly ICustomerRepository _customerRepository;

        public AutopilotController()
            : this(new InMemoryMovieRepository(), new InMemoryRentalRepository(),
                   new InMemoryCustomerRepository())
        {
        }

        public AutopilotController(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository,
            ICustomerRepository customerRepository)
        {
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
            _service = new AutopilotService(
                movieRepository, rentalRepository, customerRepository, new SystemClock());
        }

        // GET: Autopilot
        public ActionResult Index(int? customerId)
        {
            var customers = _customerRepository.GetAll().OrderBy(c => c.Name).ToList();

            if (!customerId.HasValue)
            {
                return View(new AutopilotViewModel
                {
                    Customers = customers
                });
            }

            try
            {
                var vm = _service.GetDashboard(customerId.Value);
                return View(vm);
            }
            catch (KeyNotFoundException ex)
            {
                return View(new AutopilotViewModel
                {
                    Customers = customers,
                    SelectedCustomerId = customerId,
                    ErrorMessage = ex.Message
                });
            }
        }

        // POST: Autopilot/UpdateProfile
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult UpdateProfile(int customerId, bool enabled,
            string[] favoriteGenres, string[] moodPreferences,
            string decadePreference, int maxQueueSize)
        {
            var profile = _service.GetOrCreateProfile(customerId);
            profile.Enabled = enabled;
            profile.FavoriteGenres = (favoriteGenres ?? new string[0])
                .Select(g => { Genre parsed; return Enum.TryParse(g, out parsed) ? (Genre?)parsed : null; })
                .Where(g => g.HasValue)
                .Select(g => g.Value)
                .ToList();
            profile.MoodPreferences = (moodPreferences ?? new string[0]).ToList();
            profile.DecadePreference = decadePreference;
            profile.MaxQueueSize = Math.Max(1, Math.Min(10, maxQueueSize));

            _service.UpdateProfile(customerId, profile);

            return RedirectToAction("Index", new { customerId });
        }

        // POST: Autopilot/Generate
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Generate(int customerId)
        {
            _service.GenerateWeeklyQueue(customerId);
            return RedirectToAction("Index", new { customerId });
        }

        // POST: Autopilot/Accept
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Accept(int customerId, int movieId)
        {
            _service.AcceptPick(customerId, movieId);
            return RedirectToAction("Index", new { customerId });
        }

        // POST: Autopilot/Skip
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Skip(int customerId, int movieId)
        {
            _service.SkipPick(customerId, movieId);
            return RedirectToAction("Index", new { customerId });
        }
    }
}
