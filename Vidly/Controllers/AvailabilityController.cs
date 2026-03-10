using System;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    public class AvailabilityController : Controller
    {
        private readonly AvailabilityService _availabilityService;

        /// <summary>
        /// Parameterless constructor for ASP.NET MVC default controller factory.
        /// </summary>
        public AvailabilityController()
            : this(
                new InMemoryMovieRepository(),
                new InMemoryRentalRepository(),
                new InMemoryReservationRepository())
        {
        }

        /// <summary>
        /// Constructor injection for testability.
        /// </summary>
        public AvailabilityController(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository,
            IReservationRepository reservationRepository = null)
        {
            _availabilityService = new AvailabilityService(
                movieRepository, rentalRepository, reservationRepository);
        }

        // GET: Availability
        public ActionResult Index(Genre? genre, bool? availableOnly, string query,
            int? days, string tab)
        {
            var calendarDays = days ?? 14;
            var showAvailableOnly = availableOnly ?? false;

            var viewModel = new AvailabilityViewModel
            {
                Movies = _availabilityService.GetAllAvailability(genre, showAvailableOnly, query),
                Calendar = _availabilityService.GetAvailabilityCalendar(calendarDays),
                Summary = _availabilityService.GetSummary(),
                SelectedGenre = genre,
                AvailableOnly = showAvailableOnly,
                Query = query,
                CalendarDays = calendarDays,
                ActiveTab = string.IsNullOrEmpty(tab) ? "list" : tab
            };

            return View(viewModel);
        }

        // GET: Availability/Movie/5
        public ActionResult Movie(int id)
        {
            var availability = _availabilityService.GetMovieAvailability(id);
            if (availability == null)
                return HttpNotFound("Movie not found.");

            return View(availability);
        }

        // GET: Availability/ComingSoon
        public ActionResult ComingSoon(int? days)
        {
            var withinDays = days ?? 3;
            var movies = _availabilityService.GetComingSoon(withinDays);
            ViewBag.WithinDays = withinDays;
            return View(movies);
        }
    }
}
