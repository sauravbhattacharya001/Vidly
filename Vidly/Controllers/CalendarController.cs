using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Visual monthly calendar showing rental events (checkouts, due dates, returns).
    /// GET /Calendar              — current month
    /// GET /Calendar?year=2026&amp;month=3  — specific month
    /// GET /Calendar?customerId=1 — filtered by customer
    /// GET /Calendar/Upcoming     — upcoming due dates (next 7 days)
    /// </summary>
    public class CalendarController : Controller
    {
        private readonly RentalCalendarService _calendarService;
        private readonly ICustomerRepository _customerRepository;

        public CalendarController()
        {
            var rentalRepo = new InMemoryRentalRepository();
            var customerRepo = new InMemoryCustomerRepository();
            _calendarService = new RentalCalendarService(rentalRepo, customerRepo);
            _customerRepository = customerRepo;
        }

        internal CalendarController(
            RentalCalendarService calendarService,
            ICustomerRepository customerRepository)
        {
            _calendarService = calendarService
                ?? throw new ArgumentNullException(nameof(calendarService));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
        }

        /// <summary>
        /// Monthly calendar view with rental events.
        /// </summary>
        [HttpGet]
        public ActionResult Index(int? year, int? month, int? customerId)
        {
            var now = DateTime.Today;
            var y = year ?? now.Year;
            var m = month ?? now.Month;

            try
            {
                var calendar = _calendarService.GetCalendarMonth(y, m, customerId);

                ViewBag.Customers = _customerRepository.GetAll()
                    .OrderBy(c => c.Name)
                    .ToList();

                return View(calendar);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction("Index");
            }
        }

        /// <summary>
        /// Upcoming due dates in the next N days.
        /// </summary>
        [HttpGet]
        public ActionResult Upcoming(int? days, int? customerId)
        {
            var upcoming = _calendarService.GetUpcomingEvents(days ?? 7, customerId);

            ViewBag.Customers = _customerRepository.GetAll()
                .OrderBy(c => c.Name)
                .ToList();
            ViewBag.Days = days ?? 7;

            return View(upcoming);
        }
    }
}
