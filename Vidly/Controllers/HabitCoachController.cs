using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Rental Habit Coach — autonomous analysis of customer rental patterns
    /// with personalized coaching goals and actionable nudges.
    /// </summary>
    public class HabitCoachController : Controller
    {
        private readonly HabitCoachService _service;
        private readonly ICustomerRepository _customerRepository;

        public HabitCoachController()
            : this(new InMemoryMovieRepository(), new InMemoryRentalRepository(),
                   new InMemoryCustomerRepository())
        {
        }

        public HabitCoachController(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository,
            ICustomerRepository customerRepository)
        {
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
            _service = new HabitCoachService(
                rentalRepository, movieRepository, customerRepository, new SystemClock());
        }

        // GET: HabitCoach
        public ActionResult Index(int? customerId)
        {
            var customers = _customerRepository.GetAll().OrderBy(c => c.Name).ToList();
            var viewModel = new HabitCoachViewModel
            {
                Customers = customers,
                SelectedCustomerId = customerId
            };

            if (customerId.HasValue)
            {
                try
                {
                    viewModel.Report = _service.Analyze(customerId.Value);
                }
                catch (KeyNotFoundException ex)
                {
                    viewModel.ErrorMessage = ex.Message;
                }
            }

            return View(viewModel);
        }
    }
}
