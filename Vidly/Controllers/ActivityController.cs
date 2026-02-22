using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Customer rental activity and history reports.
    /// GET /Activity          — customer selector
    /// GET /Activity?customerId=X — full activity report
    /// </summary>
    public class ActivityController : Controller
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly CustomerActivityService _activityService;

        public ActivityController()
        {
            _customerRepository = new InMemoryCustomerRepository();
            _activityService = new CustomerActivityService(
                new InMemoryCustomerRepository(),
                new InMemoryMovieRepository(),
                new InMemoryRentalRepository());
        }

        // For testing
        internal ActivityController(
            ICustomerRepository customerRepository,
            CustomerActivityService activityService)
        {
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _activityService = activityService
                ?? throw new ArgumentNullException(nameof(activityService));
        }

        /// <summary>
        /// Shows customer activity report. If no customerId is provided,
        /// shows a customer selector.
        /// </summary>
        [HttpGet]
        public ActionResult Index(int? customerId)
        {
            ViewBag.Customers = _customerRepository.GetAll()
                .OrderBy(c => c.Name)
                .ToList();

            if (!customerId.HasValue)
            {
                return View("Index", model: null as CustomerActivityReport);
            }

            try
            {
                var report = _activityService.GetActivityReport(customerId.Value);
                return View("Index", report);
            }
            catch (System.Collections.Generic.KeyNotFoundException)
            {
                TempData["Error"] = "Customer not found.";
                return View("Index", model: null as CustomerActivityReport);
            }
        }
    }
}
