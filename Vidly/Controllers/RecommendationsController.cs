using System;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    public class RecommendationsController : Controller
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly IRentalRepository _rentalRepository;
        private readonly RecommendationService _recommendationService;

        /// <summary>
        /// Parameterless constructor for ASP.NET MVC default controller factory.
        /// </summary>
        public RecommendationsController()
            : this(
                new InMemoryCustomerRepository(),
                new InMemoryMovieRepository(),
                new InMemoryRentalRepository())
        {
        }

        /// <summary>
        /// Constructor injection for testability and future DI container use.
        /// </summary>
        public RecommendationsController(
            ICustomerRepository customerRepository,
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository)
        {
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
            _recommendationService = new RecommendationService(movieRepository, rentalRepository);
        }

        // GET: Recommendations
        public ActionResult Index(int? customerId)
        {
            var viewModel = new RecommendationViewModel
            {
                Customers = _customerRepository.GetAll(),
                SelectedCustomerId = customerId
            };

            if (customerId.HasValue)
            {
                var customer = _customerRepository.GetById(customerId.Value);
                if (customer == null)
                    return HttpNotFound("Customer not found.");

                viewModel.SelectedCustomerName = customer.Name;
                viewModel.Result = _recommendationService.GetRecommendations(
                    customerId.Value, maxRecommendations: 10);
            }

            return View(viewModel);
        }
    }
}
