using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.Models;

namespace Vidly.Controllers
{
    /// <summary>
    /// Taste Evolution Tracker — analyzes how a customer's movie preferences
    /// change over time, detects genre drift, predicts future taste, and
    /// proactively suggests movies matching their evolving profile.
    /// </summary>
    public class TasteEvolutionController : Controller
    {
        private readonly TasteEvolutionService _service;
        private readonly ICustomerRepository _customerRepository;

        public TasteEvolutionController()
            : this(new InMemoryMovieRepository(), new InMemoryRentalRepository(),
                   new InMemoryCustomerRepository())
        {
        }

        public TasteEvolutionController(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository,
            ICustomerRepository customerRepository)
        {
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
            _service = new TasteEvolutionService(
                rentalRepository, movieRepository, customerRepository, new SystemClock());
        }

        // GET: TasteEvolution
        public ActionResult Index(int? customerId)
        {
            var customers = _customerRepository.GetAll().OrderBy(c => c.Name).ToList();
            var viewModel = new TasteEvolutionViewModel
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
