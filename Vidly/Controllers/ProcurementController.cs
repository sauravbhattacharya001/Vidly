using System;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Catalog Procurement Advisor — autonomous movie acquisition recommendation engine.
    /// Analyzes rental demand patterns, identifies genre supply gaps, forecasts ROI
    /// for potential acquisitions, and produces budget allocation plans.
    /// </summary>
    public class ProcurementController : Controller
    {
        private readonly ProcurementAdvisorService _service;

        public ProcurementController()
            : this(
                new InMemoryMovieRepository(),
                new InMemoryRentalRepository(),
                new InMemoryCustomerRepository(),
                new SystemClock())
        {
        }

        public ProcurementController(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository,
            ICustomerRepository customerRepository,
            IClock clock)
        {
            if (movieRepository == null) throw new ArgumentNullException("movieRepository");
            if (rentalRepository == null) throw new ArgumentNullException("rentalRepository");
            if (customerRepository == null) throw new ArgumentNullException("customerRepository");
            if (clock == null) throw new ArgumentNullException("clock");
            _service = new ProcurementAdvisorService(movieRepository, rentalRepository, customerRepository, clock);
        }

        // GET: Procurement
        public ActionResult Index()
        {
            var report = _service.Analyze();
            return View(report);
        }

        // GET: Procurement/WithBudget?amount=500&strategy=Balanced
        public ActionResult WithBudget(decimal amount = 250, string strategy = "Balanced")
        {
            BudgetAllocationStrategy strat;
            if (!Enum.TryParse(strategy, true, out strat))
                strat = BudgetAllocationStrategy.Balanced;

            var report = _service.Analyze(amount, strat);
            return View("Index", report);
        }
    }
}
