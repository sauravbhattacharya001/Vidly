using System;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    public class MovieNightController : Controller
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly MovieNightPlannerService _plannerService;

        public MovieNightController()
            : this(
                new InMemoryCustomerRepository(),
                new InMemoryMovieRepository(),
                new InMemoryRentalRepository())
        {
        }

        public MovieNightController(
            ICustomerRepository customerRepository,
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository)
        {
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _plannerService = new MovieNightPlannerService(
                movieRepository ?? throw new ArgumentNullException(nameof(movieRepository)),
                rentalRepository ?? throw new ArgumentNullException(nameof(rentalRepository)));
        }

        // GET: MovieNight
        public ActionResult Index()
        {
            var viewModel = new MovieNightViewModel
            {
                Customers = _customerRepository.GetAll(),
                Themes = _plannerService.GetAvailableThemes()
            };
            return View(viewModel);
        }

        // POST: MovieNight/Plan
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Plan(MovieNightRequest request)
        {
            if (request == null)
                return RedirectToAction("Index");

            var plan = _plannerService.GeneratePlan(request);

            var viewModel = new MovieNightViewModel
            {
                Customers = _customerRepository.GetAll(),
                Themes = _plannerService.GetAvailableThemes(),
                Request = request,
                Plan = plan
            };

            return View("Index", viewModel);
        }
    }
}
